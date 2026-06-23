using System.Collections.Generic;
using UnityEngine;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;

namespace ScpAgent.Bot.Sensors.Modules.Memory
{
    /// <summary>
    /// Gestor genérico de memoria visual. 
    /// Instanciar uno por cada tipo: new VisualMemory<ObjectMemoryDoor>(10f);
    /// </summary>
    public class VisualMemory<T> where T : ObjectMemory, new()
    {
        private readonly Dictionary<int, T> _memoria = new Dictionary<int, T>();
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
        /// Obtiene la memoria existente o crea una nueva, actualizando los campos base.
        /// Retorna el objeto para que el llamador asigne las propiedades específicas.
        /// </summary>
        public T ObtenerORegistrar(int id, Vector3 posicion, float tiempoActual, object referencia)
        {
            if (!_memoria.TryGetValue(id, out var mem))
            {
                mem = new T();
                _memoria[id] = mem;
            }
            
            mem.UltimaPosicion   = posicion;
            mem.UltimoTimestamp  = tiempoActual;
            mem.VistoEsteCiclo   = true;
            mem.ReferenciaObjeto = referencia;
            
            return mem;
        }

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

        public bool TryGet(int id, out T mem) => _memoria.TryGetValue(id, out mem);
        
        public IEnumerable<KeyValuePair<int, T>> Entradas => _memoria;

        public void Clear() => _memoria.Clear();
        public int Count => _memoria.Count;
    }
}

