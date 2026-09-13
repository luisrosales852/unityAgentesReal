import argparse
import copy
import json
import math
import random
import tempfile
from pathlib import Path
import sys

CANDIDATOS = (
    Path(__file__).resolve().parents[1] / "MultiAgents",
    Path(__file__).resolve().parents[2] / "MultiAgents",
    Path.home() / "MultiAgents",
    Path.home() / "Documents" / "MultiAgents",
    Path.home() / "Desktop" / "MultiAgents",
)

def proyecto_por_defecto():
    for ruta in CANDIDATOS:
        if (ruta / "python" / "simulation.py").is_file():
            return ruta
    return CANDIDATOS[0]

DEFAULT_PROJECT = proyecto_por_defecto()

BASE_POR_DEFECTO = "P31,P29,P25,P21,P19"

BANDA_ANCHO = 0.97
BANDA_FONDO = 1.25

MUELLE_ANCHO = 2.71
MUELLE_FONDO = 1.83

RACK_FONDO = 0.90
RACK_HOLGURA = 0.29

OBSTACULOS = [
    (-2.50, -1.50, 0.7),
    (-2.75,  3.50, 0.7),
    (-7.00, -4.50, 0.7),
]

MARGEN_BANDA = 0.20
# A 0.50 cada caja tapa exactamente una arista; con 0.45 no tapaban ninguna.
MARGEN_OBSTACULO = 0.50
NOMBRES_OBSTACULO = ("Obstaculo_1", "Obstaculo_2", "Obstaculo_3")
OBSTACULO_CICLO = 180
OBSTACULO_DURACION = 70
OBSTACULO_DESFASE = 60
PENALIZACION = 1e4

RETRANQUEO_PIEZA = 0.45

NODOS_DE_LLEGADA = ("B1", "B2", "B3")

CAJAS_POR_BANDA = 2

PRIMERA_LLEGADA = 1
CADA_CUANTOS_PASOS = 14

NIVEL_DE_LLEGADA = 2

AGVS_DE_ENTRADA = 2

FLOTA_DEDICADA = "dedicada"
FLOTA_LIBRE = "libre"
FLOTAS = (FLOTA_DEDICADA, FLOTA_LIBRE)
FLOTA_POR_DEFECTO = FLOTA_DEDICADA

PESO_POR_CAJA = 25.0

PESO_POR_ESPERA = 0.6

TOPE_DEL_BONO = 150.0

PASOS_PARA_APARCAR = 6

ALTO_DE_LA_BANDA = 0.24

ROLES_DE_BANDA = ("conveyor", "production")

def nodos_con_rol(raw, *roles):
    return {n: v for n, v in raw.get("nodes", {}).items()
            if v.get("role") in roles and n in raw.get("positions", {})}

def nodos_de_banda(raw):
    pos = raw.get("positions", {})
    nombres = set(nodos_con_rol(raw, *ROLES_DE_BANDA))
    nombres |= {n for n in NODOS_DE_LLEGADA if n in pos}
    return sorted(nombres)

def _centrado(x, z, ancho, fondo):
    return (x - ancho / 2, z - fondo / 2, x + ancho / 2, z + fondo / 2)

def rectangulos_de_banda(raw):
    pos = raw.get("positions", {})
    return [_centrado(float(pos[n][0]), float(pos[n][1]), BANDA_ANCHO, BANDA_FONDO)
            for n in nodos_de_banda(raw)]

def rectangulos_de_muelle(raw):
    pos = raw.get("positions", {})
    return [_centrado(float(pos[n][0]), float(pos[n][1]), MUELLE_ANCHO, MUELLE_FONDO)
            for n in nodos_con_rol(raw, "dock")]

def filas_de_rack(raw):
    pos = raw.get("positions", {})
    filas = {}
    for n in nodos_con_rol(raw, "storage"):
        filas.setdefault(round(float(pos[n][1]), 1), []).append(n)
    return filas

def rack_de_abajo(raw):
    filas = filas_de_rack(raw)
    if not filas:
        return [], []
    z = min(filas)
    nodos = filas[z]
    pos = raw.get("positions", {})
    xs = [float(pos[n][0]) for n in nodos]
    rect = (min(xs) - RACK_HOLGURA, z - RACK_FONDO / 2,
            max(xs) + RACK_HOLGURA, z + RACK_FONDO / 2)
    return [rect], nodos

def piezas_que_no_se_pisan(raw):
    return rectangulos_de_banda(raw) + rectangulos_de_muelle(raw) + rack_de_abajo(raw)[0]

def nodos_exentos(raw):
    return (tuple(nodos_de_banda(raw)) + tuple(nodos_con_rol(raw, "dock"))
            + tuple(rack_de_abajo(raw)[1]))

def zonas_vetadas(raw):
    return [(caja, MARGEN_BANDA) for caja in piezas_que_no_se_pisan(raw)]

def zonas_de_obstaculo():
    return [((cx - lado / 2, cz - lado / 2, cx + lado / 2, cz + lado / 2),
             MARGEN_OBSTACULO) for cx, cz, lado in OBSTACULOS]

def _cruza(p, q, caja, margen):
    x0, z0 = caja[0] - margen, caja[1] - margen
    x1, z1 = caja[2] + margen, caja[3] + margen
    t0, t1 = 0.0, 1.0
    for pi, qi, lo, hi in ((p[0], q[0], x0, x1), (p[1], q[1], z0, z1)):
        d = qi - pi
        if abs(d) < 1e-9:
            if pi < lo or pi > hi:
                return False
            continue
        a, b = (lo - pi) / d, (hi - pi) / d
        if a > b:
            a, b = b, a
        t0, t1 = max(t0, a), min(t1, b)
        if t0 > t1:
            return False
    return True

def veta_zonas(warehouse, positions, raw, exentos=()):
    zonas = zonas_vetadas(raw)
    exentos = set(exentos)
    tocadas = 0
    for a, vecinos in warehouse.adjacency.items():
        if a not in positions:
            continue
        for b in list(vecinos):
            if b not in positions:
                continue
            if a in exentos or b in exentos:
                continue
            if any(_cruza(positions[a], positions[b], caja, margen)
                   for caja, margen in zonas):
                vecinos[b] += PENALIZACION
                tocadas += 1
    return tocadas

def aristas_por_obstaculo(warehouse, positions, exentos=()):
    """Que aristas tapa cada caja negra. Se mide una vez; luego solo se enciende y apaga."""
    exentos = set(exentos)
    por_caja = []
    for caja, margen in zonas_de_obstaculo():
        tocadas = []
        for a, vecinos in warehouse.adjacency.items():
            if a not in positions or a in exentos:
                continue
            for b in vecinos:
                if b not in positions or b in exentos:
                    continue
                if _cruza(positions[a], positions[b], caja, margen):
                    tocadas.append((a, b))
        por_caja.append(tocadas)
    return por_caja

class CajasQueEstorban:
    """Las cajas negras del almacen: aparecen, tapan lo suyo y se las llevan."""

    def __init__(self, warehouse, aristas):
        self.warehouse = warehouse
        self.aristas = aristas
        self.base = {(a, b): warehouse.adjacency[a][b]
                     for lista in aristas for a, b in lista}
        self.puestas = [None] * len(aristas)

    @staticmethod
    def puesta_en(paso, i):
        arranca = i * OBSTACULO_DESFASE
        return paso >= arranca and (paso - arranca) % OBSTACULO_CICLO < OBSTACULO_DURACION

    def aplica(self, paso):
        ahora = [self.puesta_en(paso, i) for i in range(len(self.aristas))]
        if ahora == self.puestas:
            return
        self.puestas = ahora
        for (a, b), peso in self.base.items():
            self.warehouse.adjacency[a][b] = peso
        for i, puesta in enumerate(ahora):
            if puesta:
                for a, b in self.aristas[i]:
                    self.warehouse.adjacency[a][b] += PENALIZACION
        cache = getattr(self.warehouse, "_rutas", None)
        if isinstance(cache, dict):
            cache.clear()

    def payload(self):
        return [{"id": NOMBRES_OBSTACULO[i] if i < len(NOMBRES_OBSTACULO) else f"Obstaculo_{i+1}",
                 "active": bool(self.puestas[i]),
                 "x": OBSTACULOS[i][0], "z": OBSTACULOS[i][1]}
                for i in range(len(self.aristas))]

def racks_con_hueco(raw, nivel=NIVEL_DE_LLEGADA):
    ocupadas = {
        caja["node"] for caja in raw.get("boxes", ()) if caja.get("level") == nivel
    }
    return [
        nodo
        for nodo, meta in sorted(raw.get("nodes", {}).items())
        if meta.get("role") == "storage" and nodo not in ocupadas
    ]

def mapa_con_llegadas(raw, por_banda=CAJAS_POR_BANDA):
    raw = copy.deepcopy(raw)
    if por_banda <= 0:
        return raw, []

    nodos = [n for n in NODOS_DE_LLEGADA if n in raw.get("nodes", {})]
    if not nodos:
        raise ValueError(
            f"el mapa no tiene ninguno de los nodos de banda {NODOS_DE_LLEGADA}: "
            f"sin ellos no hay por donde meter las cajas"
        )

    niveles = raw.get("coordinate_system", {}).get("levels", [1, 2])
    if por_banda > len(niveles):
        raise ValueError(
            f"caben {len(niveles)} caja(s) por banda como mucho y se pidieron "
            f"{por_banda}: el mapa declara los niveles {list(niveles)} y dos cajas "
            f"no pueden compartir hueco. Sube --agents o anade nodos de banda, "
            f"pero no niveles: las estanterias solo tienen esas baldas."
        )

    raw.setdefault("roles", {})["production"] = (
        "linea de entrada: por aqui llegan al almacen las cajas de fuera"
    )
    for nodo in nodos:
        raw["nodes"][nodo]["role"] = "production"

    alto = float(raw.get("coordinate_system", {}).get("caja_offset_y", 0.218))
    nuevas = []

    for ronda in range(por_banda):
        for nodo in nodos:
            nx, nz = raw["positions"][nodo]
            nuevas.append({
                "id": f"IN{len(nuevas) + 1:02d}",
                "node": nodo,

                "level": ronda + 1,

                "unity_object": "",

                "unity_local": [nx, round(ALTO_DE_LA_BANDA + alto, 3), nz],
            })

    raw.setdefault("boxes", []).extend(nuevas)
    return raw, [caja["id"] for caja in nuevas]

def solo_puja_por(agente, flujo):
    original = type(agente).bid

    def bid(bus, t, pool, chargers=()):
        return original(agente, bus, t,
                        [m for m in pool if m.flow is flujo], chargers)

    return bid

class BonoDeBanda:

    """Lo que suma a la puja una mision de la banda. Crece con las cajas que se juntan en la linea y..."""

    def __init__(self, flujo, por_caja=PESO_POR_CAJA, por_espera=PESO_POR_ESPERA,
                 tope=TOPE_DEL_BONO):
        self.flujo = flujo
        self.por_caja = float(por_caja)
        self.por_espera = float(por_espera)
        self.tope = float(tope)

    def presion(self, t, pool):
        banda = [m for m in pool if m.flow is self.flujo]
        if not banda:
            return 0, 0
        espera = max(t - (m.t_publicada if m.t_publicada is not None else t)
                     for m in banda)
        return len(banda), espera

    def __call__(self, t, pool):
        cuantas, espera = self.presion(t, pool)
        if cuantas == 0:
            return 0.0
        return min(self.por_caja * cuantas + self.por_espera * espera, self.tope)

def puja_con_prioridad(agente, bono, missions):
    """El bid de siempre, con el bono de la banda sumado."""

    original = type(agente).bid

    def bid(bus, t, pool, chargers=()):

        aparte = missions.MessageBus()
        pujas = original(agente, aparte, t, pool, chargers)

        extra = bono(t, pool)
        de_banda = {m.id for m in pool if m.flow is bono.flujo}
        ajustadas = [(mision, utilidad + (extra if mision in de_banda else 0.0))
                     for mision, utilidad in pujas]

        subida = dict(ajustadas)
        for mensaje in aparte.history:
            if mensaje.tipo is missions.MessageType.BID:
                mensaje.contenido["utilidad"] = round(
                    subida[mensaje.contenido["mision"]], 2)
            bus.publish(mensaje)
        return ajustadas

    return bid

def por_la_mejor_puja(pendientes, pujas):
    """Ordena la bolsa por la mejor puja que recibio cada mision."""

    mejor = {}
    for mensaje in pujas:
        mision = mensaje.contenido.get("mision")
        utilidad = mensaje.contenido.get("utilidad")
        if mision is None or utilidad is None:
            continue
        if mision not in mejor or utilidad > mejor[mision]:
            mejor[mision] = utilidad

    return sorted(pendientes, key=lambda m: -mejor.get(m.id, -math.inf))

def media(valores):
    return round(sum(valores) / len(valores), 1) if valores else 0.0

def grafo_parcheado(graph, raw, nombre):
    with tempfile.TemporaryDirectory() as carpeta:
        destino = Path(carpeta) / f"{nombre}.json"
        destino.write_text(json.dumps(raw), encoding="utf-8")
        return graph.load_graph(destino)

def load_backend(project):
    project = Path(project).expanduser().resolve()
    if (project / "MultiAgents" / "python" / "simulation.py").is_file():
        project /= "MultiAgents"
    source = project / "python"
    if not (source / "simulation.py").is_file():
        raise ValueError(f"No se encontro MultiAgents/python/simulation.py en {project}")
    sys.path.insert(0, str(source))
    import config
    import graph
    import missions
    import server
    import simulation
    return config, graph, server, simulation, missions

def rutas_desde_la_base(config, simulation, warehouse, base, agents):
    nodos = warehouse.nodes()
    conocidos = set(nodos)
    faltan = [n for n in base if n not in conocidos]
    if faltan:
        raise ValueError(f"la base nombra nodos que no estan en el mapa: {faltan}")
    if len(base) < agents:
        raise ValueError(
            f"la base tiene {len(base)} nodo(s) y hacen falta {agents}: cada AGV "
            f"necesita el suyo. Anade nodos con --base o baja --agents."
        )

    por_defecto = simulation.default_route(warehouse)
    destinos = [por_defecto[1]]
    if agents > 1:
        rng = random.Random(config.RANDOM_SEED)
        rng.sample([n for n in nodos if n != por_defecto[0]], agents - 1)
        destinos += rng.sample([n for n in nodos if n != por_defecto[1]], agents - 1)

    return list(zip(base[:agents], destinos))

def create_simulation(backend, map_name="almacen_reto", agents=1, policy="qlearning",
                      base=None, llegadas=CAJAS_POR_BANDA, prioriza_la_banda=False,
                      agvs_de_entrada=AGVS_DE_ENTRADA, flota=FLOTA_POR_DEFECTO,
                      peso_por_caja=PESO_POR_CAJA, peso_por_espera=PESO_POR_ESPERA,
                      tope_del_bono=TOPE_DEL_BONO, ritmo=CADA_CUANTOS_PASOS):
    config, graph, _, simulation, missions = backend
    if flota not in FLOTAS:
        raise ValueError(
            f"no conozco el reparto {flota!r}; los que hay son " + ", ".join(FLOTAS)
        )
    map_path = config.MAPS_DIR / f"{map_name}.json"
    raw = json.loads(map_path.read_text(encoding="utf-8"))
    raw, ids_que_llegan = mapa_con_llegadas(raw, llegadas)
    huecos = racks_con_hueco(raw)
    warehouse = grafo_parcheado(graph, raw, map_name)
    warehouse.validate()
    vetadas = veta_zonas(warehouse, raw["positions"], raw, exentos=nodos_exentos(raw))
    cajas_negras = CajasQueEstorban(
        warehouse, aristas_por_obstaculo(warehouse, raw["positions"],
                                         exentos=nodos_exentos(raw)))
    piezas_rect = piezas_que_no_se_pisan(raw)
    ultima_buena = {}
    nodos_de_entrada = [n for n in NODOS_DE_LLEGADA if n in warehouse.positions]
    visual_boxes = {box["id"]: box for box in raw.get("boxes", [])}
    coordinates = raw.get("coordinate_system", {})
    flujo_banda = missions.Flow.PRODUCTION_TO_RACK
    bono = BonoDeBanda(flujo_banda, peso_por_caja, peso_por_espera, tope_del_bono)

    class ManagerConHuecos(missions.MissionManager):

        def __init__(self, bus, grafo):
            super().__init__(bus, grafo)
            self._huecos = list(huecos)
            self._por_puja = False
            self._t = None

        def subasta_por_puja(self, si):

            self._por_puja = bool(si)

        def publish(self, t):
            self._t = t
            return super().publish(t)

        def pool(self):
            pendientes = super().pool()
            if prioriza_la_banda:
                pendientes.sort(
                    key=lambda m: 0 if m.flow is flujo_banda else 1
                )
            if self._por_puja and self._t is not None:
                pendientes = por_la_mejor_puja(
                    pendientes, self.bus.read(missions.MessageType.BID, self._t)
                )
            return pendientes

        def open_work(self, inventory):
            nuevas = super().open_work(inventory)
            for mision in nuevas:
                if mision.flow is not missions.Flow.PRODUCTION_TO_RACK:
                    continue
                hueco = self._hueco_mas_cerca(mision.node)
                if hueco is not None:
                    self._huecos.remove(hueco)
                    mision.destination = hueco
            return nuevas

        def _hueco_mas_cerca(self, desde):
            mejor, mejores_ticks = None, None
            for hueco in self._huecos:
                ticks = self.graph.route_ticks(desde, hueco)
                if ticks is None:
                    continue
                if mejores_ticks is None or ticks < mejores_ticks:
                    mejor, mejores_ticks = hueco, ticks
            return mejor

    class UnitySimulation(simulation.Simulation):

        _por_llegar = ()

        fleet = flota
        fleets = FLOTAS

        def _prepara_llegadas(self):
            self.manager = ManagerConHuecos(self.bus, self.graph)
            self._recogidas = {}
            self._sin_nada = {}
            self._por_llegar = [
                (PRIMERA_LLEGADA + numero * ritmo, caja)
                for numero, caja in enumerate(
                    filter(None, (self.inventory.pop(i, None) for i in ids_que_llegan))
                )
            ]

        def set_fleet(self, reparto):
            reparto = str(reparto).strip().lower()
            if reparto not in FLOTAS:
                raise ValueError(
                    f"no conozco el reparto {reparto!r}; los que hay son "
                    + ", ".join(FLOTAS)
                )
            with self._lock:
                self.fleet = reparto
                # Mismos obstaculos que la corrida anterior: cambiar de reparto
                # es para comparar los dos, y no se comparan en almacenes distintos.
                self.reset(resortea_obstaculos=False)
                return self.fleet

        def _reparte_la_flota(self):
            self.de_entrada = []
            self.manager.subasta_por_puja(False)

            for agente in self.agents:
                agente.__dict__.pop("bid", None)

            if not self.deliveries:
                return
            if self.fleet == FLOTA_LIBRE:
                self._flota_libre()
            elif ids_que_llegan and nodos_de_entrada:
                self._flota_dedicada()

        def _flota_libre(self):

            self.manager.subasta_por_puja(True)
            for agente in self.agents:
                agente.bid = puja_con_prioridad(agente, bono, missions)

        def _flota_dedicada(self):
            cuantos = min(agvs_de_entrada, len(self.agents) - 1)
            if cuantos <= 0:
                return

            def lejania(agente):
                ticks = [self.graph.route_ticks(agente.current_node, nodo)
                         for nodo in nodos_de_entrada]
                ticks = [t for t in ticks if t is not None]
                return min(ticks) if ticks else math.inf

            cerca = sorted(self.agents, key=lambda a: (lejania(a), a.id))
            de_entrada = {a.id for a in cerca[:cuantos]}
            for agente in self.agents:
                agente.bid = solo_puja_por(
                    agente,
                    missions.Flow.PRODUCTION_TO_RACK if agente.id in de_entrada
                    else missions.Flow.RACK_TO_DOCK,
                )
            self.de_entrada = sorted(de_entrada)

        def _hay_trabajo_de_banda(self):
            if self._por_llegar:
                return True
            return any(
                m.flow is missions.Flow.PRODUCTION_TO_RACK
                and m.status is not missions.MissionStatus.COMPLETED
                for m in self.manager.missions.values()
            )

        def _aparca_a_los_que_esperan(self):

            libre = self.fleet == FLOTA_LIBRE
            if not libre and not self.de_entrada:
                return

            for agente in self.agents:
                if not libre and agente.id not in self.de_entrada:
                    continue
                if agente.state not in (simulation.State.IDLE, simulation.State.DONE):
                    self._sin_nada[agente.id] = 0
                    continue
                if agente.mission is not None or agente.carrying:
                    self._sin_nada[agente.id] = 0
                    continue

                self._sin_nada[agente.id] = self._sin_nada.get(agente.id, 0) + 1
                if agente.current_node in nodos_de_entrada:
                    continue

                if libre and self._sin_nada[agente.id] < PASOS_PARA_APARCAR:
                    continue

                destino = self._banda_libre(agente)
                if destino is not None:
                    agente.assign_task(agente.current_node, destino, task=agente.task)

        def _banda_libre(self, agente):
            tomadas = {
                otro.current_node for otro in self.agents if otro.id != agente.id
            } | {
                otro.target_node for otro in self.agents
                if otro.id != agente.id and otro.target_node is not None
            }
            opciones = [
                (t, nodo)
                for nodo in nodos_de_entrada
                if nodo not in tomadas
                and (t := self.graph.route_ticks(agente.current_node, nodo)) is not None
            ]
            return min(opciones)[1] if opciones else None

        def _suelta_las_que_tocan(self):
            while self._por_llegar and self._por_llegar[0][0] <= self.step + 1:
                _, caja = self._por_llegar.pop(0)

                caja.level = NIVEL_DE_LLEGADA
                self.inventory[caja.id] = caja

        def tick(self):

            with self._lock:
                cajas_negras.aplica(self.step)
                self._suelta_las_que_tocan()
                paso = super().tick()
                self._apunta_las_recogidas()
                self._aparca_a_los_que_esperan()
                return paso

        def _apunta_las_recogidas(self):

            for mensaje in self.bus.read(missions.MessageType.PICKED_UP, self.step):
                self._recogidas.setdefault(mensaje.contenido["mision"], mensaje.t)

        def metricas(self):

            with self._lock:
                pendientes = self.manager.pool()
                esperando, la_mas_vieja = bono.presion(self.step, pendientes)

                banda, muelle = [], []
                for mision in self.manager.missions.values():
                    (banda if mision.flow is flujo_banda else muelle).append(mision)

                esperas = [
                    self._recogidas[m.id] - m.t_publicada
                    for m in banda
                    if m.id in self._recogidas and m.t_publicada is not None
                ]
                ciclos = {
                    "banda": self._ciclos(banda), "muelle": self._ciclos(muelle),
                }

                return {
                    "mode": self.fleet,
                    "modes": list(FLOTAS),
                    "dedicated": list(self.de_entrada),
                    "weight_box": bono.por_caja,
                    "weight_wait": bono.por_espera,
                    "belt_total": len(ids_que_llegan),
                    "belt_arrived": len(banda),
                    "belt_pending": esperando,
                    "belt_oldest": la_mas_vieja,
                    "belt_bonus": round(bono(self.step, pendientes), 1),
                    "belt_picked": len(esperas),
                    "belt_wait_avg": media(esperas),
                    "belt_wait_max": max(esperas) if esperas else 0,
                    "belt_stored": len(ciclos["banda"]),
                    "belt_cycle_avg": media(ciclos["banda"]),
                    "dock_done": len(ciclos["muelle"]),
                    "dock_cycle_avg": media(ciclos["muelle"]),
                    "per_agv": [a.completed for a in self.agents],
                }

        @staticmethod
        def _ciclos(misiones):
            return [
                m.t_completada - m.t_publicada
                for m in misiones
                if m.t_completada is not None and m.t_publicada is not None
            ]

        def done(self):
            with self._lock:
                if self._por_llegar:
                    return False
                return super().done()

        def reset(self, *, resortea_obstaculos=True):
            super().reset(resortea_obstaculos=resortea_obstaculos)
            cajas_negras.aplica(0)
            ultima_buena.clear()
            for agente in self.agents:
                agente.mission = None
                agente.battery = config.BATTERY_FULL
                agente.charges = 0
                agente.completed = 0

            self._prepara_llegadas()

            self._reparte_la_flota()

        def pisa_una_pieza(self, x, z):
            r = RETRANQUEO_PIEZA
            for x0, z0, x1, z1 in piezas_rect:
                if x0 - r <= x <= x1 + r and z0 - r <= z <= z1 + r:
                    return True
            return False

        def empuja_fuera(self, x, z):
            r = RETRANQUEO_PIEZA
            for x0, z0, x1, z1 in piezas_rect:
                a0, b0, a1, b1 = x0 - r, z0 - r, x1 + r, z1 + r
                if not (a0 <= x <= a1 and b0 <= z <= b1):
                    continue
                dx, dz = min(((a0 - x, 0.0), (a1 - x, 0.0),
                              (0.0, b0 - z), (0.0, b1 - z)),
                             key=lambda d: abs(d[0]) + abs(d[1]))
                return x + dx, z + dz
            return x, z

        def retrocede_hasta_salir(self, previa, x, z):
            lejos, cerca = 0.0, 1.0
            for _ in range(24):
                t = (lejos + cerca) / 2
                if self.pisa_una_pieza(previa[0] + (x - previa[0]) * t,
                                      previa[1] + (z - previa[1]) * t):
                    cerca = t
                else:
                    lejos = t
            return (previa[0] + (x - previa[0]) * lejos,
                    previa[1] + (z - previa[1]) * lejos)

        def frena_ante_la_pieza(self, agente):
            aid = agente["id"]
            x, z = agente["x"], agente["z"]
            if not self.pisa_una_pieza(x, z):
                ultima_buena[aid] = (x, z)
                return False

            previa = ultima_buena.get(aid)
            if previa is not None and not self.pisa_una_pieza(previa[0], previa[1]):
                nx, nz = self.retrocede_hasta_salir(previa, x, z)
            else:
                nx, nz = self.empuja_fuera(x, z)

            agente["x"], agente["z"] = nx, nz
            if not self.pisa_una_pieza(nx, nz):
                ultima_buena[aid] = (nx, nz)
            return True

        def snapshot(self):
            with self._lock:
                state = super().snapshot()
                state["obstacles"] = cajas_negras.payload()
                for agente in state["agents"]:
                    self.frena_ante_la_pieza(agente)
                carriers = {a["carrying"]: a for a in state["agents"] if a["carrying"]}
                for box in state["boxes"]:
                    visual = visual_boxes[box["id"]]
                    box["unity_object"] = visual.get("unity_object", "")
                    carrier = carriers.get(box["id"])

                    box["conveyor"] = (
                        carrier is None
                        and warehouse.role_of(box["node"]) == graph.ROLE_PRODUCTION
                    )
                    if carrier is not None:
                        x, y, z = carrier["x"], carrier["y"], carrier["z"]
                    elif box["node"] == visual["node"] and box["status"] != "DELIVERED":

                        local = visual.get("unity_local")
                        if local is not None:
                            x, y, z = (float(v) * config.UNITY_SCALE for v in local)
                        else:
                            x, y, z = self.box_position(box)
                    else:
                        x, y, z = self.box_position(box)
                    box.update(x=x, y=y, z=z)
                state["bridge"] = {"protocol": "agv-unity/1", "map": warehouse.name}
                state["fleet"] = self.metricas()
                return state

        def box_position(self, box):
            px, py = raw["positions"][box["node"]]
            x, _, z = graph.to_unity(px, py)
            height = float(coordinates.get("caja_offset_y", 0.218))
            if warehouse.role_of(box["node"]) == graph.ROLE_STORAGE:
                height += float(coordinates.get("level_base", 0.154))
                height += (box["level"] - 1) * float(coordinates.get("level_height", 0.69))
            return x, height * config.UNITY_SCALE, z

    rutas = (rutas_desde_la_base(config, simulation, warehouse, base, agents)
             if base else None)
    return UnitySimulation(warehouse, agents, policy=policy, deliveries=True,
                           routes=rutas)

def main():
    parser = argparse.ArgumentParser(description="Conecta el proyecto MultiAgents con WebClient de Unity")
    parser.add_argument("--project", type=Path, default=DEFAULT_PROJECT,
                        help="Carpeta AGV Aaron o MultiAgents")
    parser.add_argument("--port", type=int, default=5055)

    parser.add_argument("--agents", type=int, default=5)
    parser.add_argument("--map", default="almacen_reto")

    parser.add_argument("--policy", default="qlearning")
    parser.add_argument("--base", default=BASE_POR_DEFECTO,
                        help="Nodos de salida separados por comas, en orden de id: el "
                             "primero es el AGV 1. Por defecto la estacion de carga, "
                             "para que no aparezcan repartidos por el almacen. Pasa "
                             "--base '' para el reparto aleatorio del simulador.")
    parser.add_argument("--llegadas", type=int, default=CAJAS_POR_BANDA,
                        help="Cajas que entran por cada banda a lo largo de la "
                             "corrida. Con 0 el almacen arranca como antes, sin "
                             "linea de entrada.")
    parser.add_argument("--ritmo", type=int, default=CADA_CUANTOS_PASOS,
                        help="Pasos entre una caja y la siguiente en la linea de "
                             "entrada. Cuanto mas bajo, mas cajas se juntan en la "
                             "banda.")
    parser.add_argument("--agvs-de-entrada", type=int, default=AGVS_DE_ENTRADA,
                        help="Cuantos AGV se dedican solo a la banda. Los demas "
                             "solo sacan cajas al muelle. Con 0 todos hacen de "
                             "todo, y entonces la banda tarda en atenderse. Solo "
                             "cuenta con --flota dedicada.")
    parser.add_argument("--flota", default=FLOTA_POR_DEFECTO, choices=FLOTAS,
                        help="Como se reparte el trabajo. 'dedicada': unos AGV "
                             "solo hacen banda -> estanteria y el resto solo "
                             "estanteria -> muelle. 'libre': ninguno tiene flujo "
                             "asignado y la banda gana la subasta cuando se le "
                             "juntan cajas. Se puede cambiar en caliente desde "
                             "Unity (tecla F) o con POST /fleet.")
    parser.add_argument("--peso-banda", type=float, default=PESO_POR_CAJA,
                        help="Con --flota libre, cuanto suma a la puja cada caja "
                             "que espera en la banda. Subelo y la banda se "
                             "atiende antes; bajalo y manda la distancia.")
    parser.add_argument("--peso-espera", type=float, default=PESO_POR_ESPERA,
                        help="Con --flota libre, cuanto suma a la puja cada paso "
                             "que lleva esperando la caja mas vieja de la banda. "
                             "Evita que una caja sola se quede ahi para siempre.")
    parser.add_argument("--tope-banda", type=float, default=TOPE_DEL_BONO,
                        help="Techo del bono de la banda, para que el muelle no "
                             "se quede sin atender del todo.")
    parser.add_argument("--prioriza-banda", action="store_true",
                        help="Atender la banda antes que las estanterias. La "
                             "primera caja se recoge sobre el paso 65 en vez del "
                             "874, pero el almacen se agarrota sobre el 431 en "
                             "vez de aguantar mas de 1500. Para grabar el ciclo "
                             "entero sin esperar.")
    args = parser.parse_args()
    base = [n.strip() for n in args.base.split(",") if n.strip()]
    try:
        backend = load_backend(args.project)
        backend[0].setup_logging()
        sim = create_simulation(backend, args.map, args.agents, args.policy, base,
                                args.llegadas, args.prioriza_banda,
                                args.agvs_de_entrada, args.flota,
                                args.peso_banda, args.peso_espera, args.tope_banda,
                                args.ritmo)
    except (OSError, ValueError, ImportError) as exc:
        parser.exit(2, f"No se pudo iniciar la conexion: {exc}\n")
    print(f"Python conectado a: {backend[0].PROJECT_ROOT}", flush=True)
    salidas = ", ".join(a.current_node for a in sim.agents)
    print(f"Unity: http://127.0.0.1:{args.port} | {args.agents} AGV(s) | "
          f"{args.policy} | {len(sim.inventory)} cajas", flush=True)
    print(f"Salen de: {salidas}", flush=True)
    if getattr(sim, "de_entrada", None):
        otros = [a.id for a in sim.agents if a.id not in sim.de_entrada]
        print(f"Reparto: flota {sim.fleet}. AGV {sim.de_entrada} solo banda -> "
              f"estanteria; AGV {otros} solo estanteria -> muelle. Cuando no "
              f"quede caja por entrar, los de la banda esperan a pie de riel.",
              flush=True)
    elif sim.fleet == FLOTA_LIBRE:
        print(f"Reparto: flota libre. Los {args.agents} AGV pujan por todo; cada "
              f"caja parada en la banda suma {args.peso_banda:g} a la puja y cada "
              f"paso de espera {args.peso_espera:g}, con tope {args.tope_banda:g}. "
              f"Se cambia de reparto en caliente con la tecla F en Unity.",
              flush=True)
    print(f"Zonas vetadas: {len(NODOS_DE_LLEGADA)} bandas + {len(OBSTACULOS)} obstaculos "
          f"(las bandas se miden del mapa; los AGV frenan a {RETRANQUEO_PIEZA} m)",
          flush=True)
    entran = args.llegadas * len(NODOS_DE_LLEGADA)
    if entran:
        print(f"Linea de entrada: {entran} cajas por {', '.join(NODOS_DE_LLEGADA)}, "
              f"la primera en el paso {PRIMERA_LLEGADA} y una mas cada "
              f"{args.ritmo}. Van de la banda a la estanteria y de ahi "
              f"al muelle.", flush=True)
    print("Abre Assets/Scenes/Almacen_AGV.unity y pulsa Play. Ctrl+C para cerrar.", flush=True)
    return backend[2].serve_forever(sim, host="127.0.0.1", port=args.port)

if __name__ == "__main__":
    if sys.version_info < (3, 10):
        raise SystemExit("Se necesita Python 3.10 o posterior.")
    raise SystemExit(main())
