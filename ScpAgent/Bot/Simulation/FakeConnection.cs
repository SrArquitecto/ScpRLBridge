using System;

using Mirror;


namespace ScpAgent.Bot.Simulation
{
    
    public class FakeConnection : NetworkConnectionToClient
    {
        public FakeConnection(int networkConnectionId) : base(networkConnectionId)
        {
        }

        // Desviamos los paquetes al vacío para que no consuma recursos de red
        public override void Send(ArraySegment<byte> segment, int channelId = 0)
        {
        }

        // 🌟 CORRECCIÓN AQUÍ: Customizamos el método de desconexión nativo
        public override void Disconnect()
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