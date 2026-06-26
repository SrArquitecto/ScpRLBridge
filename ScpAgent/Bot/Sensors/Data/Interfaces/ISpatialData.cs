namespace ScpAgent.Bot.Sensors.Data.Interfaces
{
    public interface ISpatialData
    {
        string Type { get; set; }
        float Distance { get; set; }
        float RelX { get; set; }
        float RelY { get; set; }
        float RelZ { get; set; }
        float RealRelX { get; set; }
        float RealRelY { get; set; }
        float RealRelZ { get; set; }
        bool EsRecordado { get; set; }
        float Antiguedad { get; set; }
    }
}
