using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RecruitAI.Infrastructure.Interviews;

namespace RecruitAI.Tests;

public sealed class TencentInterviewGatewayTests
{
    [Fact]
    public void CreateUserSig_ProducesTencentTlsPayloadWithoutExposingSecret()
    {
        var settings = new TencentInterviewSettings(true, "secret-id", "secret-key", 1400000001, "sdk-secret", "bucket-1", "ap-shanghai", "16k_zh");
        var gateway = new TencentInterviewGateway(new HttpClient(), settings);

        var userSig = gateway.CreateUserSig("candidate-42", 3600);
        var compressed = Convert.FromBase64String(userSig.Replace('*', '+').Replace('-', '/').Replace('_', '='));
        using var input = new MemoryStream(compressed);
        using var zip = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zip.CopyTo(output);
        using var payload = JsonDocument.Parse(output.ToArray());

        Assert.Equal("2.0", payload.RootElement.GetProperty("TLS.ver").GetString());
        Assert.Equal("candidate-42", payload.RootElement.GetProperty("TLS.identifier").GetString());
        Assert.Equal(1400000001, payload.RootElement.GetProperty("TLS.sdkappid").GetInt32());
        Assert.Equal(3600, payload.RootElement.GetProperty("TLS.expire").GetInt32());
        Assert.DoesNotContain("sdk-secret", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
    }
}
