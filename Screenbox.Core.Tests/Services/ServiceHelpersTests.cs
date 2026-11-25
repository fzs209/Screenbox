using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Windows.Foundation.Collections;

namespace Screenbox.Core.Tests.Services;

public class ServiceHelpersTests
{
    [Test]
    public async Task PopulateCoreServices_RegistersSourceAndPlaybackMetadataServices()
    {
        ServiceCollection services = new();
        ServiceHelpers.PopulateCoreServices(services);
        services.AddLogging();

        using ServiceProvider provider = services.BuildServiceProvider();

        ISourceMetadataService sourceService = provider.GetRequiredService<ISourceMetadataService>();
        IPlaybackMetadataService playbackService = provider.GetRequiredService<IPlaybackMetadataService>();

        await Assert.That(sourceService).IsNotNull();
        await Assert.That(sourceService).IsTypeOf<StorageMetadataService>();

        await Assert.That(playbackService).IsNotNull();
        await Assert.That(playbackService).IsTypeOf<MfMetadataService>();
    }

    [Test]
    public async Task PopulateCoreServices_ResolvesPropertyViewModel()
    {
        ServiceCollection services = new();
        ServiceHelpers.PopulateCoreServices(services);
        services.AddLogging();
        services.AddSingleton<ISettingsStorage>(new SettingsStorage(new PropertySet()));

        using ServiceProvider provider = services.BuildServiceProvider();

        PropertyViewModel propertyVm = provider.GetRequiredService<PropertyViewModel>();
        await Assert.That(propertyVm).IsNotNull();
    }
}
