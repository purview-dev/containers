using System.Globalization;
using System.Text;

namespace Purview.Containers.Azurite;

/// <summary>A throwaway Azurite (Azure Storage emulator) instance running on WSL Containers.</summary>
public sealed class AzuriteContainer : ContainerBase
{
	internal AzuriteContainer(AzuriteConfiguration configuration, IContainerBackend? backend)
		: base(configuration, backend) { }

	/// <summary>Blob service endpoint. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public Uri GetBlobEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(AzuriteBuilder.BlobPort)}/{AzuriteAccount.Name}");
	}

	/// <summary>Queue service endpoint. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public Uri GetQueueEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(AzuriteBuilder.QueuePort)}/{AzuriteAccount.Name}");
	}

	/// <summary>Table service endpoint. Safe to call after <see cref="IContainer.StartAsync" />.</summary>
	public Uri GetTableEndpoint()
	{
		return new Uri($"http://127.0.0.1:{GetMappedPublicPort(AzuriteBuilder.TablePort)}/{AzuriteAccount.Name}");
	}

	/// <summary>Azure Storage connection string for the emulator. The account key comes from
	/// <see cref="AzuriteAccount.Key" /> (a placeholder until the consuming repository supplies it).</summary>
	public string GetConnectionString()
	{
		StringBuilder builder = new();
		builder.Append("DefaultEndpointsProtocol=http;");
		builder.Append(CultureInfo.InvariantCulture, $"AccountName={AzuriteAccount.Name};");
		builder.Append(CultureInfo.InvariantCulture, $"AccountKey={AzuriteAccount.Key};");
		builder.Append(CultureInfo.InvariantCulture, $"BlobEndpoint={GetBlobEndpoint()};");
		builder.Append(CultureInfo.InvariantCulture, $"QueueEndpoint={GetQueueEndpoint()};");
		builder.Append(CultureInfo.InvariantCulture, $"TableEndpoint={GetTableEndpoint()};");
		return builder.ToString();
	}
}
