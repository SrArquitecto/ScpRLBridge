

namespace ScpAgent.Network.Event
{
    public class AgentHandshakeEventArgs : System.EventArgs
    {
        public int AgentId { get; }
        public string RoleType { get; }

        public AgentHandshakeEventArgs(int agentId, string roleType)
        {
            AgentId = agentId;
            RoleType = roleType;
        }
    }
}
