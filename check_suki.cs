using System;
using SukiUI;

class Program {
    static void Main() {
        var styles = Enum.GetNames(typeof(SukiUI.Enums.SukiBackgroundStyle));
        foreach(var s in styles) Console.WriteLine(s);
    }
}
