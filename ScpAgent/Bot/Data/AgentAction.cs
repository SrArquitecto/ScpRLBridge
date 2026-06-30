namespace ScpAgent.Bot.Data
{
    public class AgentAction
    {
        // Multi-discreto de 7 ejes (todos en el mismo step físico).
        // ActionIds[0] = long,    [1] = lat,     [2] = yaw,   [3] = pitch,
        // ActionIds[4] = inv,     [5] = interact,[6] = jump
        public int[] ActionIds { get; set; } = new int[7];
    }
}
