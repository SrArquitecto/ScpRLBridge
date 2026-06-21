using System.Collections.Generic;
using UnityEngine;

namespace ScpAgent.Bot.Sensors
{
    /// <summary>
    /// Memoria de un objeto estático individual (puerta, keycard, locker, lift, room).
    /// </summary>
    public class ObjectMemory
    {
        public Vector3 UltimaPosicion;
        public float   UltimoTimestamp;
        public bool    VistoEsteCiclo;

        // Snapshot del estado relevante en el momento de verlo (ej. IsOpen de una puerta)
        // Se actualiza solo cuando se ve directamente — al recordar, se usa el último conocido.
        public bool  EstadoBoolCache;   // p.ej. IsOpen, HasIsOpen
        public int   EstadoIntCache;    // p.ej. RequiredTier
    }

    /// <summary>
    /// Gestor genérico de memoria visual para colecciones de objetos estáticos.
    /// Cada AgentSensors mantiene una instancia por tipo de objeto (puertas, keycards, etc.)
    /// usando un Dictionary[int, MemoriaObjeto] keyado por InstanceID o un ID estable.
    /// </summary>
    public class VisualMemory
    {
        private readonly Dictionary<int, ObjectMemory> _memoria = new Dictionary<int, ObjectMemory>();
        private readonly List<int> _idsAEliminar = new List<int>(8);

        public readonly float TiempoOlvido;

        public VisualMemory(float tiempoOlvidoSegundos)
        {
            TiempoOlvido = tiempoOlvidoSegundos;
        }

        public void MarcarTodosNoVistos()
        {
            foreach (var mem in _memoria.Values)
                mem.VistoEsteCiclo = false;
        }

        /// <summary>
        /// Registra que un objeto fue visto directamente este ciclo.
        /// </summary>
        public ObjectMemory RegistrarVisto(int id, Vector3 posicion, float tiempoActual,
            bool estadoBool = false, int estadoInt = 0)
        {
            if (!_memoria.TryGetValue(id, out var mem))
            {
                mem = new ObjectMemory();
                _memoria[id] = mem;
            }
            mem.UltimaPosicion   = posicion;
            mem.UltimoTimestamp  = tiempoActual;
            mem.VistoEsteCiclo   = true;
            mem.EstadoBoolCache  = estadoBool;
            mem.EstadoIntCache   = estadoInt;
            return mem;
        }

        /// <summary>
        /// Purga entradas demasiado antiguas. Llamar una vez por ciclo tras procesar
        /// todos los objetos visibles.
        /// </summary>
        public void PurgarOlvidados(float tiempoActual)
        {
            _idsAEliminar.Clear();
            foreach (var kv in _memoria)
            {
                if (kv.Value.VistoEsteCiclo) continue;
                if (tiempoActual - kv.Value.UltimoTimestamp > TiempoOlvido)
                    _idsAEliminar.Add(kv.Key);
            }
            foreach (var id in _idsAEliminar)
                _memoria.Remove(id);
        }

        /// <summary>
        /// Devuelve true si el objeto está en memoria (visto ahora o recordado) y rellena
        /// los datos de salida. No distingue entre visto/recordado — eso lo decide el caller
        /// comparando mem.VistoEsteCiclo.
        /// </summary>
        public bool TryGet(int id, out ObjectMemory mem) => _memoria.TryGetValue(id, out mem);

        public IEnumerable<KeyValuePair<int, ObjectMemory>> Entradas => _memoria;

        public void Clear() => _memoria.Clear();

        public int Count => _memoria.Count;
    }
}
