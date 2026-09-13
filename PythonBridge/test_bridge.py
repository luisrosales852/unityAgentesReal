import json
import logging
import unittest

from serve_unity import (
    FLOTA_DEDICADA,
    FLOTA_LIBRE,
    FLOTAS,
    DEFAULT_PROJECT,
    create_simulation,
    load_backend,
)

class BridgeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        logging.disable(logging.CRITICAL)
        cls.backend = load_backend(DEFAULT_PROJECT)
        cls.raw = json.loads((cls.backend[0].MAPS_DIR / "almacen_reto.json").read_text())

    def setUp(self):
        self.sim = create_simulation(self.backend)

    def test_state_does_not_advance_and_keeps_all_box_positions(self):
        first = self.sim.snapshot()
        self.assertEqual(first, self.sim.snapshot())
        self.assertEqual(first["step"], 0)
        expected = {box["id"]: box for box in self.raw["boxes"]}
        self.assertEqual(len(first["boxes"]), 30)
        for box in first["boxes"]:
            self.assertEqual(box["unity_object"], expected[box["id"]]["unity_object"])
            self.assertEqual([box[k] for k in ("x", "y", "z")], expected[box["id"]]["unity_local"])

    def test_pick_transport_delivery_and_reset(self):
        initial = self.sim.snapshot()
        carried = delivered = False
        for step in range(1, 301):
            state = self.sim.get_snapshot()
            self.assertEqual(state["step"], step)
            for box in state["boxes"]:
                if box["status"] == "IN_TRANSIT":
                    carrier = next(a for a in state["agents"] if a["carrying"] == box["id"])
                    self.assertEqual([box[k] for k in ("x", "y", "z")],
                                     [carrier[k] for k in ("x", "y", "z")])
                    carried = True
                if box["status"] == "DELIVERED":
                    self.assertEqual([box["x"], box["z"]], self.raw["positions"][box["node"]])
                    self.assertAlmostEqual(box["y"], 0.218)
                    delivered = True
            if carried and delivered:
                break
        self.assertTrue(carried)
        self.assertTrue(delivered)
        self.sim.reset()
        reset = self.sim.snapshot()
        self.assertEqual(reset["step"], 0)
        self.assertEqual(reset["stats"]["run"], initial["stats"]["run"] + 1)
        self.assertEqual(reset["boxes"], initial["boxes"])

    @staticmethod
    def sin_lo_visual(agentes):
        return [{k: v for k, v in a.items() if k not in ("x", "y", "z")}
                for a in agentes]

    def test_adapter_preserves_original_simulation_decisions(self):
        sim = create_simulation(self.backend, llegadas=0)
        original = self.backend[3].Simulation(sim.graph, 1, deliveries=True)
        for _ in range(100):
            actual = sim.get_snapshot()
            expected = original.get_snapshot()
            self.assertEqual(self.sin_lo_visual(actual["agents"]),
                             self.sin_lo_visual(expected["agents"]))
            self.assertEqual(actual["stats"], expected["stats"])

    def test_multiple_agents_and_policy_switch(self):
        sim = create_simulation(self.backend, agents=4)
        state = sim.get_snapshot()
        self.assertEqual([a["id"] for a in state["agents"]], [1, 2, 3, 4])
        sim.set_mode("qlearning")
        state = sim.get_snapshot()
        self.assertEqual(state["mode"], "qlearning")
        self.assertEqual(state["step"], 1)
        del_mapa = {caja["id"] for caja in self.raw["boxes"]}
        self.assertTrue(all(box["unity_object"] for box in state["boxes"]
                            if box["id"] in del_mapa))

class FlotaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        logging.disable(logging.CRITICAL)
        cls.backend = load_backend(DEFAULT_PROJECT)

    def sim(self, **kwargs):
        kwargs.setdefault("agents", 5)
        return create_simulation(self.backend, **kwargs)

    def test_dedicated_splits_the_fleet_and_free_does_not(self):
        dedicada = self.sim(flota=FLOTA_DEDICADA)
        self.assertEqual(len(dedicada.de_entrada), 2)
        self.assertFalse(dedicada.manager._por_puja)

        libre = self.sim(flota=FLOTA_LIBRE)
        self.assertEqual(libre.de_entrada, [])
        self.assertTrue(libre.manager._por_puja)

    def test_free_fleet_lets_everyone_bid_for_the_belt(self):
        sim = self.sim(flota=FLOTA_LIBRE)
        misiones = self.backend[4]
        for _ in range(60):
            sim.tick()

        de_banda = {
            m.contenido["mision"]
            for m in sim.bus.read(misiones.MessageType.MISSION_PUBLISHED)
            if m.contenido["flujo"] is misiones.Flow.PRODUCTION_TO_RACK
        }
        self.assertTrue(de_banda, "no llego a publicarse ninguna mision de banda")

        pujaron = {
            m.contenido["agv"]
            for m in sim.bus.read(misiones.MessageType.BID)
            if m.contenido["mision"] in de_banda
        }
        self.assertGreater(len(pujaron), 2)

    def test_the_weight_only_lifts_the_belt_and_grows_with_the_queue(self):
        sim = self.sim(flota=FLOTA_LIBRE)
        misiones = self.backend[4]

        primero = None
        for _ in range(80):
            sim.tick()
            banda = sim.metricas()
            if banda["belt_pending"] >= 1 and primero is None:
                primero = banda
            if primero is not None and banda["belt_pending"] > primero["belt_pending"]:
                self.assertGreater(banda["belt_bonus"], primero["belt_bonus"])
                break

        self.assertIsNotNone(primero, "la banda nunca tuvo cajas esperando")
        self.assertGreater(primero["belt_bonus"], 0.0)

        vacia = create_simulation(self.backend, agents=5, flota=FLOTA_LIBRE,
                                  llegadas=0)
        vacia.tick()
        self.assertEqual(vacia.metricas()["belt_bonus"], 0.0)

    def test_the_weight_gets_the_belt_served_sooner(self):
        esperas = {}
        for peso in (0.0, 25.0):
            sim = self.sim(flota=FLOTA_LIBRE, peso_por_caja=peso,
                           peso_por_espera=0.0)
            for _ in range(700):
                sim.tick()
                if sim.done():
                    break
            esperas[peso] = sim.metricas()["belt_wait_avg"]

        self.assertGreater(esperas[0.0], 0.0)
        self.assertLess(esperas[25.0], esperas[0.0])

    def test_switching_fleet_starts_a_clean_run(self):
        sim = self.sim(flota=FLOTA_DEDICADA)
        for _ in range(50):
            sim.tick()
        corrida = sim.stats()["run"]

        self.assertEqual(sim.set_fleet(FLOTA_LIBRE), FLOTA_LIBRE)
        estado = sim.snapshot()
        self.assertEqual(estado["step"], 0)
        self.assertEqual(estado["stats"]["run"], corrida + 1)
        self.assertEqual(estado["fleet"]["mode"], FLOTA_LIBRE)
        self.assertEqual(estado["fleet"]["dedicated"], [])
        self.assertEqual(estado["fleet"]["modes"], list(FLOTAS))

        self.assertEqual(sim.set_fleet(FLOTA_DEDICADA), FLOTA_DEDICADA)
        self.assertEqual(sim.snapshot()["fleet"]["dedicated"], [3, 4])
        with self.assertRaises(ValueError):
            sim.set_fleet("inventada")

    def test_dedicated_still_runs_the_original_auction(self):
        antes = self.sim(flota=FLOTA_DEDICADA)
        despues = self.sim(flota=FLOTA_LIBRE)
        despues.set_fleet(FLOTA_DEDICADA)

        for _ in range(120):
            antes.tick()
            despues.tick()

        self.assertEqual(
            [(a.id, a.current_node, a.mission) for a in antes.agents],
            [(a.id, a.current_node, a.mission) for a in despues.agents],
        )

if __name__ == "__main__":
    unittest.main()
