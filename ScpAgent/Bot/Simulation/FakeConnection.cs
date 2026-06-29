using System;

using Mirror;


namespace ScpAgent.Bot.Simulation
{

    public class FakeConnection : NetworkConnectionToClient
    {
        // Conexión real subyacente (la del dummy generado por DummyUtils.SpawnDummy).
        // Si es null, FakeConnection actúa como fake puro (paquetes descartados).
        // Si está set, los paquetes se reenvían al cliente real para que el sync funcione.
        private NetworkConnectionToClient _realConn;

        public FakeConnection(int networkConnectionId) : base(networkConnectionId)
        {
        }

        // Constructor para envolver la conexión real de un dummy (DummyUtils.SpawnDummy).
        // Equivalente a: fakeConn.SetRealConnection(dummy.connectionToClient)
        public FakeConnection(int networkConnectionId, NetworkConnectionToClient realConn)
            : base(networkConnectionId)
        {
            _realConn = realConn;
        }

        // Permite inyectar la conexión real del dummy después de spawnearlo
        public void SetRealConnection(NetworkConnectionToClient realConn)
        {
            _realConn = realConn;
        }

        // Si hay conexión real, reenvía los paquetes. Si no, los descarta
        // (comportamiento fake original).
        public override void Send(ArraySegment<byte> segment, int channelId = 0)
        {
            if (_realConn != null)
                _realConn.Send(segment, channelId);
        }

        // 🌟 CORRECCIÓN AQUÍ: Customizamos el método de desconexión nativo
        public override void Disconnect()
        {
            if (_realConn != null)
            {
                // Si tenemos conexión real (dummy), desconectamos esa
                _realConn.Disconnect();
            }
            else
            {
                // 1. Destruimos el avatar físico (GameObject) del bot en el servidor a través de Mirror
                NetworkServer.DestroyPlayerForConnection(this);

                // 2. Lo eliminamos del registro oficial de conexiones activas del servidor
                if (NetworkServer.connections.ContainsKey(connectionId))
                {
                    NetworkServer.connections.Remove(connectionId);
                }
            }
        }
    }
}
