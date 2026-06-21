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
        public object  ReferenciaObjeto;
    }

    public class ObjectMemoryDoor : ObjectMemory
    {
        // Snapshot del estado relevante en el momento de verlo (ej. IsOpen de una puerta)
        // Se actualiza solo cuando se ve directamente — al recordar, se usa el último conocido.
        public bool  PuertaAbierta;   // p.ej. IsOpen, HasIsOpen
        public int   PermisoPuerta;    // p.ej. RequiredTier

    }

    public class ObjectMemoryLift : ObjectMemory
    {
        // Snapshot del estado relevante en el momento de verlo (ej. IsOpen de una puerta)
        // Se actualiza solo cuando se ve directamente — al recordar, se usa el último conocido.
        public bool  AscensorMoviendose;  
        public bool AscensorOperativo; 
        public bool  AscensorCerrado; 
        public bool  PuedeUsarse;   
        public int NivelActual;  

    }

    /// <summary>
    /// Gestor genérico de memoria visual para colecciones de objetos estáticos.
    /// Cada AgentSensors mantiene una instancia por tipo de objeto (puertas, keycards, etc.)
    /// usando un Dictionary[int, MemoriaObjeto] keyado por InstanceID o un ID estable.
    /// </summary>
    public class VisualMemory
    {
        private readonly Dictionary<int, ObjectMemoryDoor> _memoriaPuerta = new Dictionary<int, ObjectMemoryDoor>();
        private readonly Dictionary<int, ObjectMemoryLift> _memoriaLift = new Dictionary<int, ObjectMemoryLift>();
        private readonly List<int> _idsAEliminar = new List<int>(8);

        public readonly float TiempoOlvido;

        public VisualMemory(float tiempoOlvidoSegundos)
        {
            TiempoOlvido = tiempoOlvidoSegundos;
        }

        public void MarcarTodosNoVistosPuerta()
        {
            foreach (var mem in _memoriaPuerta.Values)
                mem.VistoEsteCiclo = false;
        }
        public void MarcarTodosNoVistosLift()
        {
            foreach (var mem in _memoriaLift.Values)
                mem.VistoEsteCiclo = false;
        }

        /// <summary>
        /// Registra que un objeto fue visto directamente este ciclo.
        /// </summary>
        public ObjectMemory RegistrarVistoPuerta(int id, Vector3 posicion, float tiempoActual,
            bool abierta, int permiso)
        {
            if (!_memoriaPuerta.TryGetValue(id, out var mem))
            {
                mem = new ObjectMemoryDoor();
                _memoriaPuerta[id] = mem;
            }
            mem.UltimaPosicion   = posicion;
            mem.UltimoTimestamp  = tiempoActual;
            mem.VistoEsteCiclo   = true;
            mem.PuertaAbierta  = abierta;
            mem.PermisoPuerta   = permiso;
            return mem;
        }
        public ObjectMemory RegistrarVistoLift(int id, Vector3 posicion, float tiempoActual,
            bool cerrado, bool operativo, bool moviendose, int nivel)
        {
            if (!_memoriaLift.TryGetValue(id, out var mem))
            {
                mem = new ObjectMemoryLift();
                _memoriaLift[id] = mem;
            }
            mem.UltimaPosicion   = posicion;
            mem.UltimoTimestamp  = tiempoActual;
            mem.VistoEsteCiclo   = true;
            mem.AscensorCerrado  = cerrado;
            mem.AscensorOperativo = operativo;
            mem.AscensorMoviendose   = moviendose;
            mem.PuedeUsarse = !moviendose;
            mem.NivelActual = nivel;
            return mem;
        }

        /// <summary>
        /// Purga entradas demasiado antiguas. Llamar una vez por ciclo tras procesar
        /// todos los objetos visibles.
        /// </summary>
        public void PurgarOlvidadosPuertas(float tiempoActual)
        {
            _idsAEliminar.Clear();
            foreach (var kv in _memoriaPuerta)
            {
                if (kv.Value.VistoEsteCiclo) continue;
                if (tiempoActual - kv.Value.UltimoTimestamp > TiempoOlvido)
                    _idsAEliminar.Add(kv.Key);
            }
            foreach (var id in _idsAEliminar)
                _memoriaPuerta.Remove(id);
        }

        public void PurgarOlvidadosLift(float tiempoActual)
        {
            _idsAEliminar.Clear();
            foreach (var kv in _memoriaLift)
            {
                if (kv.Value.VistoEsteCiclo) continue;
                if (tiempoActual - kv.Value.UltimoTimestamp > TiempoOlvido)
                    _idsAEliminar.Add(kv.Key);
            }
            foreach (var id in _idsAEliminar)
                _memoriaLift.Remove(id);
        }
        /// <summary>
        /// Devuelve true si el objeto está en memoria (visto ahora o recordado) y rellena
        /// los datos de salida. No distingue entre visto/recordado — eso lo decide el caller
        /// comparando mem.VistoEsteCiclo.
        /// </summary>
        public bool TryGetDoor(int id, out ObjectMemoryDoor mem) => _memoriaPuerta.TryGetValue(id, out mem);
        public bool TryGetLift(int id, out ObjectMemoryLift mem) => _memoriaLift.TryGetValue(id, out mem);
        
        public IEnumerable<KeyValuePair<int, ObjectMemoryDoor>> EntradasPuertas => _memoriaPuerta;
        public IEnumerable<KeyValuePair<int, ObjectMemoryLift>> EntradasLifts => _memoriaLift;

        public void ClearPuertas() => _memoriaPuerta.Clear();
        public void ClearLifts() => _memoriaLift.Clear();
        public int CountPuertas => _memoriaPuerta.Count;
        public int CountLifts => _memoriaLift.Count;
    }
}
