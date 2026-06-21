using UnityEngine;

namespace ScpAgent.Bot.Sensors.Memory.Data
{
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
    public class ObjectMemoryKeycard : ObjectMemory
    {
        // Snapshot del estado relevante en el momento de verlo (ej. IsOpen de una puerta)
        // Se actualiza solo cuando se ve directamente — al recordar, se usa el último conocido.
        public ItemType Tipo;
        public int Tier;

    }
        public class ObjectMemoryLocker : ObjectMemory
    {
        // Snapshot del estado relevante en el momento de verlo (ej. IsOpen de una puerta)
        // Se actualiza solo cuando se ve directamente — al recordar, se usa el último conocido.
        public string TipoLocker;

    }
}