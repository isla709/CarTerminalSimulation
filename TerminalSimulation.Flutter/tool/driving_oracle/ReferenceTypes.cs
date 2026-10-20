// Only the DTO/interface needed to compile the original geometry source.
// No WPF application project, plugins, credentials, or generated key files are loaded.
namespace TerminalSimulation.Wpf.ViewModels
{
    public sealed class GeoPoint { public double Lat { get; set; } public double Lng { get; set; } }
}
namespace TerminalSimulation.Wpf.Services
{
    internal interface ILocationSimulationService
    {
        Task RunAsync(IReadOnlyList<ViewModels.GeoPoint> path, Func<double> speedKph,
            Action<ViewModels.GeoPoint, int> positionChanged, CancellationToken cancellationToken);
    }
}
