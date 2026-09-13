# Almacén AGV — Unity

Unity solo dibuja. Las decisiones (rutas, subastas, batería, Q-learning) las toma
el servidor de Python, y Unity le pide el estado paso a paso por HTTP.

Hace falta Unity **6000.5.7f1** y Python **3.10+**.

## Cómo se corre

**1. Arranca Python primero.** Doble clic en `Iniciar_AGV.command`, o en la terminal:

```bash
./Iniciar_AGV.command
```

Deja esa terminal abierta. Tiene que acabar así:

```
Abre Assets/Scenes/Almacen_AGV.unity y pulsa Play. Ctrl+C para cerrar.
INFO [server] servidor escuchando en http://127.0.0.1:5055
```

Si da `ModuleNotFoundError`, es que no encontró el repo del simulador
([MultiAgents](https://github.com/juangast/MultiAgents)). Pásale la ruta:

```bash
./Iniciar_AGV.command --project ~/ruta/a/MultiAgents
```

**2. En Unity**, abre la escena `Assets/Scenes/Almacen_AGV.unity` y pulsa **Play**.
En la consola debe salir:

```
[WebClient] Conectado a http://127.0.0.1:5055: 5 AGV(s), 30 cajas, modo qlearning.
```

Play **no** arranca Python: son dos procesos aparte.

**3. Para cerrar**, sal de Play y **Ctrl+C** en la terminal de Python.

## Teclas (con el foco en la vista Game)

| Tecla | Qué hace |
|---|---|
| **R** | reinicia la corrida (con obstáculos nuevos) |
| **F** | cambia el reparto de la flota, `dedicada` ↔ `libre` (mismos obstáculos, para poder comparar) |
