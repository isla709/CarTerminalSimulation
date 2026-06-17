using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync("https://raw.githubusercontent.com/modood/Administrative-divisions-of-China/master/dist/pc-code.json");
        File.WriteAllText(@"e:\Project\CarTerminalSimulation\TerminalSimulation.Wpf\Regions.json", json, System.Text.Encoding.UTF8);
        Console.WriteLine("Done");
    }
}
