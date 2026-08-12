namespace ICloudDriveSync.Sync;

/// <summary>Regras de comparação de timestamps (fonte única de verdade).</summary>
public static class TimestampRules
{
    /// <summary>
    /// Arredonda para o segundo mais próximo (espelha o icloudds/pyicloud):
    /// o iCloud armazena em UTC com precisão de segundo; o mtime local tem microssegundos.
    /// Sem esse arredondamento, comparações != disparariam upload/download em loop.
    /// </summary>
    public static DateTimeOffset RoundSeconds(DateTimeOffset dt)
    {
        var utc = dt.ToUniversalTime();
        // iCloud devolve datas com precisão de milissegundos; o mtime local com microssegundos.
        var rounded = utc.Millisecond >= 500 ? utc.AddSeconds(1) : utc;
        return new DateTimeOffset(
            rounded.Year, rounded.Month, rounded.Day,
            rounded.Hour, rounded.Minute, rounded.Second,
            TimeSpan.Zero);
    }
}
