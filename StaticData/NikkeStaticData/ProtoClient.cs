using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace NikkeStaticData;

public interface IProtoParsable<T>
{
	static abstract T ParseFrom(byte[] data);
}

public static class ProtoFetcher
{
	private static readonly HttpClient httpClient = new();

	static ProtoFetcher()
	{
		httpClient.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/octet-stream+protobuf"));
	}

	public static async Task<T?> GetProtoData<T>(string endpoint)
		where T : IProtoParsable<T>
	{
		var content = new ByteArrayContent(Array.Empty<byte>());
		content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream+protobuf");

		HttpResponseMessage? response;
		try
		{
			response = await httpClient.PostAsync(endpoint, content);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to POST to {endpoint}: {ex.Message}");
			return default;
		}

		if (!response.IsSuccessStatusCode)
		{
			Console.WriteLine($"Request to {endpoint} failed with status {response.StatusCode}");
			return default;
		}

		byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
		return T.ParseFrom(responseBytes);
	}
}
