using ICloudDriveSync.Sync;

namespace ICloudDriveSync.Tests.Sync;

public class WriteGateTests
{
    [Fact]
    public async Task SerializesConcurrentWrites()
    {
        var gate = new WriteGate();
        var active = 0;
        var maxActive = 0;
        var gateLock = new object();

        async Task Op()
        {
            await gate.WaitAsync();
            try
            {
                lock (gateLock)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }
                await Task.Delay(50);
                lock (gateLock)
                {
                    active--;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Op()));

        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task ReleasesGateAfterUse()
    {
        var gate = new WriteGate();

        await gate.WaitAsync();
        gate.Release();

        // Segunda operação entra sem espera (sem deadlock).
        await gate.WaitAsync();
        gate.Release();
    }
}