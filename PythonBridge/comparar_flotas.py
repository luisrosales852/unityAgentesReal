import argparse
import csv
import json
import logging
import random
import statistics
from pathlib import Path
import sys

from serve_unity import (
    BASE_POR_DEFECTO,
    CADA_CUANTOS_PASOS,
    CAJAS_POR_BANDA,
    DEFAULT_PROJECT,
    FLOTAS,
    PESO_POR_CAJA,
    PESO_POR_ESPERA,
    TOPE_DEL_BONO,
    create_simulation,
    load_backend,
)

RITMOS = (40, 25, 14)

CORRIDAS = 6

TOPE_DE_PASOS = 3000

HITOS = (12, 24, 36)

SEMILLA = 7

SIN_PESO = "libre sin peso"

def brazos(peso_por_caja, peso_por_espera, tope):

    return (
        ("dedicada", {"flota": "dedicada"}),
        (SIN_PESO, {"flota": "libre", "peso_por_caja": 0.0,
                    "peso_por_espera": 0.0}),
        ("libre", {"flota": "libre", "peso_por_caja": peso_por_caja,
                   "peso_por_espera": peso_por_espera, "tope_del_bono": tope}),
    )

def salidas(raw, agents, corridas, semilla=SEMILLA):

    de_paso = sorted(
        nodo for nodo, meta in raw.get("nodes", {}).items()
        if meta.get("role") == "transit"
    )
    if len(de_paso) < agents:
        raise ValueError(
            f"el mapa solo tiene {len(de_paso)} nodo(s) de paso y hacen falta "
            f"{agents}: cada AGV necesita el suyo"
        )

    por_defecto = [n.strip() for n in BASE_POR_DEFECTO.split(",") if n.strip()]
    juegos = [por_defecto[:agents]] if len(por_defecto) >= agents else []

    rng = random.Random(semilla)
    while len(juegos) < corridas:
        juego = rng.sample(de_paso, agents)
        if juego not in juegos:
            juegos.append(juego)
    return juegos[:corridas]

def corre(backend, brazo, ajustes, pasos, **kwargs):
    sim = create_simulation(backend, **ajustes, **kwargs)
    hitos = {}
    entregadas = 0

    for _ in range(pasos):
        sim.tick()
        ahora = sum(1 for c in sim.inventory.values() if c.status == "DELIVERED")
        if ahora > entregadas:
            entregadas = ahora
            for hito in HITOS:
                if entregadas >= hito and hito not in hitos:
                    hitos[hito] = sim.step
        if sim.done():
            break

    estado = sim.snapshot()
    banda, comun = estado["fleet"], estado["stats"]
    completa = entregadas == len(sim.inventory)

    return {
        "brazo": brazo,
        "flota": sim.fleet,
        "pasos": sim.step,
        "completa": completa,
        "final": comun["finished_reason"] or ("completa" if completa else "sin acabar"),
        "entregadas": entregadas,
        "cajas": len(sim.inventory),
        "banda_recogidas": banda["belt_picked"],
        "banda_espera_media": banda["belt_wait_avg"],
        "banda_espera_max": banda["belt_wait_max"],
        "banda_ciclo": banda["belt_cycle_avg"],
        "muelle_ciclo": banda["dock_cycle_avg"],
        "conflictos": comun["conflicts"],
        "recargas": comun["charges"],
        "dedicados": banda["dedicated"],
        "reparto": banda["per_agv"],
        "desbalance": max(banda["per_agv"]) - min(banda["per_agv"]),
        "hitos": hitos,
    }

def promedio(valores):
    return round(statistics.fmean(valores), 1) if valores else None

def mediana(valores):
    return round(statistics.median(valores)) if valores else None

def agrupa(filas, *claves):
    grupos = {}
    for fila in filas:
        grupos.setdefault(tuple(fila[c] for c in claves), []).append(fila)
    return grupos

def resumen_de(corridas):
    completas = [c for c in corridas if c["completa"]]
    return {
        "corridas": len(corridas),
        "completas": len(completas),
        "pasos": mediana([c["pasos"] for c in completas]),
        "entregadas": promedio([c["entregadas"] for c in corridas]),
        "banda_espera": promedio([c["banda_espera_media"] for c in corridas]),
        "banda_peor": max((c["banda_espera_max"] for c in corridas), default=0),
        "banda_ciclo": promedio([c["banda_ciclo"] for c in corridas]),
        "muelle_ciclo": promedio([c["muelle_ciclo"] for c in corridas]),
        "conflictos": promedio([c["conflictos"] for c in corridas]),
        "desbalance": promedio([c["desbalance"] for c in corridas]),
        "hitos": {
            hito: mediana([c["hitos"][hito] for c in corridas if hito in c["hitos"]])
            for hito in HITOS
        },
    }

def pinta(titulo, columnas, filas):
    anchos = [
        max(len(str(cabecera)), *(len(str(fila[i])) for fila in filas))
        for i, (cabecera, _) in enumerate(columnas)
    ]
    lineas = [
        "",
        titulo,
        "  ".join(str(c).ljust(a) for (c, _), a in zip(columnas, anchos)),
        "  ".join("-" * a for a in anchos),
    ]
    for fila in filas:
        lineas.append("  ".join(
            str(v).ljust(a) for v, a in zip(fila, anchos)
        ))
    return "\n".join(lineas)

def sin_dato(valor, sufijo=""):
    return "-" if valor is None else f"{valor}{sufijo}"

def tabla_por_ritmo(filas):
    columnas = [
        ("reparto", None), ("ritmo", None), ("acaban", None), ("pasos", None),
        ("entregadas", None), ("banda espera", None), ("banda peor", None),
        ("banda ciclo", None), ("conflictos", None), ("desbalance", None),
    ]
    cuerpo = []
    for (brazo, ritmo), corridas in sorted(
        agrupa(filas, "brazo", "ritmo").items(), key=lambda p: (p[0][1], p[0][0])
    ):
        r = resumen_de(corridas)
        cuerpo.append([
            brazo, ritmo, f"{r['completas']}/{r['corridas']}", sin_dato(r["pasos"]),
            r["entregadas"], r["banda_espera"], r["banda_peor"], r["banda_ciclo"],
            r["conflictos"], r["desbalance"],
        ])
    return pinta(
        "Por ritmo de la banda (media de todas las salidas; 'pasos' es la mediana "
        "de las corridas que acaban):", columnas, cuerpo)

def por_brazo(filas):
    orden = []
    for fila in filas:
        if fila["brazo"] not in orden:
            orden.append(fila["brazo"])
    return [(b, [f for f in filas if f["brazo"] == b]) for b in orden]

def tabla_global(filas):
    columnas = [("reparto", None), ("acaban", None), ("pasos", None),
                ("entregadas", None), ("banda espera", None), ("banda ciclo", None),
                ("muelle ciclo", None), ("conflictos", None), ("desbalance", None)]
    cuerpo = []
    for brazo, corridas in por_brazo(filas):
        r = resumen_de(corridas)
        cuerpo.append([
            brazo, f"{r['completas']}/{r['corridas']}", sin_dato(r["pasos"]),
            r["entregadas"], r["banda_espera"], r["banda_ciclo"],
            r["muelle_ciclo"], r["conflictos"], r["desbalance"],
        ])
    return pinta("Todas las corridas juntas:", columnas, cuerpo)

def tabla_de_hitos(filas):
    columnas = [("reparto", None)] + [(f"{h} cajas", None) for h in HITOS]
    cuerpo = [
        [brazo] + [sin_dato(resumen_de(corridas)["hitos"][h]) for h in HITOS]
        for brazo, corridas in por_brazo(filas)
    ]
    return pinta(
        "Pasos hasta entregar N cajas al muelle (mediana, menos es mejor):",
        columnas, cuerpo)

def tabla_detallada(filas):
    columnas = [("reparto", None), ("ritmo", None), ("salen de", None),
                ("pasos", None), ("final", None), ("entregadas", None),
                ("banda espera", None), ("conflictos", None), ("por AGV", None)]
    cuerpo = [
        [f["brazo"], f["ritmo"], ",".join(f["base"]), f["pasos"], f["final"],
         f["entregadas"], f["banda_espera_media"], f["conflictos"],
         str(f["reparto"])]
        for f in filas
    ]
    return pinta("Corrida a corrida:", columnas, cuerpo)

def lectura(filas):
    resumenes = {brazo: resumen_de(corridas) for brazo, corridas in por_brazo(filas)}
    dedicada = resumenes.get("dedicada")
    libre = resumenes.get("libre")
    control = resumenes.get(SIN_PESO)
    if dedicada is None or libre is None:
        return ""

    lineas = ["", "Lectura rapida:"]
    if control is not None:
        lineas.append(
            f"  El peso hace su trabajo: sin el, una caja de la banda espera "
            f"{control['banda_espera']:.0f} pasos a que la recojan; con el, "
            f"{libre['banda_espera']:.0f}. La dedicada se queda en "
            f"{dedicada['banda_espera']:.0f}, y es que tiene AGV aparcados a pie "
            f"de banda esperando a que caiga una caja."
        )
    else:
        lineas.append(
            f"  Banda: {libre['banda_espera']:.0f} pasos de espera con la libre "
            f"contra {dedicada['banda_espera']:.0f} con la dedicada."
        )

    lineas.append(
        f"  Lo que cuesta soltar la flota: {libre['conflictos']:.0f} conflictos de "
        f"media contra {dedicada['conflictos']:.0f}, porque los "
        f"{len(filas[0]['reparto'])} AGV se meten a la vez en los mismos pasillos "
        f"en vez de quedarse dos a pie de banda."
    )
    quedan = ((dedicada["corridas"] - dedicada["completas"])
              + (libre["corridas"] - libre["completas"]))
    lineas.append(
        f"  Corridas que acaban las {filas[0]['cajas']} cajas: "
        f"{dedicada['completas']}/{dedicada['corridas']} dedicada contra "
        f"{libre['completas']}/{libre['corridas']} libre"
        + ("; las demas se atascan o se comen el tope de pasos." if quedan else ".")
    )
    if dedicada["pasos"] and libre["pasos"]:
        pasos = libre["pasos"] - dedicada["pasos"]
        lineas.append(
            f"  De las que acaban, la libre tarda {abs(pasos)} pasos "
            f"{'menos' if pasos < 0 else 'mas'} ({libre['pasos']} contra "
            f"{dedicada['pasos']})."
        )
    lineas.append(
        f"  Reparto del trabajo: entre el AGV que mas cajas mueve y el que menos "
        f"hay {libre['desbalance']:.1f} cajas con la libre y "
        f"{dedicada['desbalance']:.1f} con la dedicada."
    )
    return "\n".join(lineas)

def guarda(filas, destino):
    destino.parent.mkdir(parents=True, exist_ok=True)
    campos = [c for c in filas[0] if c != "hitos"] + [f"paso_{h}" for h in HITOS]
    with destino.open("w", newline="", encoding="utf-8") as fichero:
        escritor = csv.DictWriter(fichero, fieldnames=campos)
        escritor.writeheader()
        for fila in filas:
            plana = {c: v for c, v in fila.items() if c != "hitos"}
            plana.update({f"paso_{h}": fila["hitos"].get(h) for h in HITOS})
            escritor.writerow(plana)

def main():
    parser = argparse.ArgumentParser(
        description="Corre el mismo almacen con la flota dedicada y con la libre "
                    "y compara que le pasa a la linea de entrada"
    )
    parser.add_argument("--project", type=Path, default=DEFAULT_PROJECT)
    parser.add_argument("--agents", type=int, default=5)
    parser.add_argument("--map", default="almacen_reto")
    parser.add_argument("--policy", default="qlearning")
    parser.add_argument("--llegadas", type=int, default=CAJAS_POR_BANDA)
    parser.add_argument("--pasos", type=int, default=TOPE_DE_PASOS,
                        help="Tope de pasos por corrida.")
    parser.add_argument("--ritmos", type=int, nargs="+", default=list(RITMOS),
                        help="Pasos entre caja y caja en la banda, uno por "
                             "escenario: cuanto mas bajo, mas cajas se juntan.")
    parser.add_argument("--corridas", type=int, default=CORRIDAS,
                        help="Juegos de nodos de salida por escenario. El primero "
                             "es el de Unity; los demas salen de un sorteo fijo, "
                             "para que las dos flotas corran lo mismo.")
    parser.add_argument("--peso-banda", type=float, default=PESO_POR_CAJA)
    parser.add_argument("--peso-espera", type=float, default=PESO_POR_ESPERA)
    parser.add_argument("--tope-banda", type=float, default=TOPE_DEL_BONO)
    parser.add_argument("--sin-control", action="store_true",
                        help="Solo dedicada contra libre, sin la corrida de "
                             "control que pone el peso de la banda a cero.")
    parser.add_argument("--detalle", action="store_true",
                        help="Ademas del resumen, una linea por corrida.")
    parser.add_argument("--csv", type=Path, default=None,
                        help="Donde dejar la tabla. Por defecto, "
                             "MultiAgents/results/comparativa_flotas.csv")
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args()

    try:
        backend = load_backend(args.project)
    except (OSError, ValueError, ImportError) as exc:
        parser.exit(2, f"No se pudo cargar el simulador: {exc}\n")

    config = backend[0]
    config.setup_logging(args.verbose)
    if not args.verbose:
        logging.disable(logging.WARNING)

    try:
        raw = json.loads(
            (config.MAPS_DIR / f"{args.map}.json").read_text(encoding="utf-8")
        )
        bases = salidas(raw, args.agents, args.corridas)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        parser.exit(2, f"No se pudo preparar el sorteo de salidas: {exc}\n")

    corriendo = brazos(args.peso_banda, args.peso_espera, args.tope_banda)
    if args.sin_control:
        corriendo = tuple(b for b in corriendo if b[0] != SIN_PESO)

    total = len(args.ritmos) * len(bases) * len(corriendo)
    filas, hecha = [], 0
    for ritmo in args.ritmos:
        for base in bases:
            for brazo, ajustes in corriendo:
                hecha += 1
                print(f"[{hecha}/{total}] {brazo}, ritmo {ritmo}, desde "
                      f"{','.join(base)}", file=sys.stderr, flush=True)
                fila = corre(
                    backend, brazo, ajustes, args.pasos,
                    map_name=args.map, agents=args.agents, policy=args.policy,
                    base=base, llegadas=args.llegadas, ritmo=ritmo,
                )
                fila["ritmo"] = ritmo
                fila["base"] = base
                filas.append(fila)

    cajas = filas[0]["cajas"]
    dedicados = next((f["dedicados"] for f in filas if f["dedicados"]), [])
    print(f"\n{args.agents} AGV, {cajas} cajas, mapa {args.map}, politica "
          f"{args.policy}, {len(bases)} juego(s) de salida x "
          f"{len(args.ritmos)} ritmo(s).")
    print(f"  dedicada:       {len(dedicados)} AGV solo hacen banda -> estanteria "
          f"(los mas cercanos a la banda) y el resto solo estanteria -> muelle.")
    print(f"  libre sin peso: los {args.agents} pujan por todo y gana la mejor "
          f"puja, sin mirar cuantas cajas hay en la banda (control).")
    print(f"  libre:          igual, pero cada caja parada en la banda suma "
          f"{args.peso_banda:g} a la puja, cada paso de espera "
          f"{args.peso_espera:g}, con tope {args.tope_banda:g}.")

    print(tabla_global(filas))
    print(tabla_por_ritmo(filas))
    print(tabla_de_hitos(filas))
    if args.detalle:
        print(tabla_detallada(filas))
    print(lectura(filas))

    destino = args.csv or (config.RESULTS_DIR / "comparativa_flotas.csv")
    guarda(filas, destino)
    print(f"\nUna linea por corrida en {destino}")
    return 0

if __name__ == "__main__":
    if sys.version_info < (3, 10):
        raise SystemExit("Se necesita Python 3.10 o posterior.")
    raise SystemExit(main())
