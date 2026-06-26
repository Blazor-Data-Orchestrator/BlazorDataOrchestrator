#:property TargetFramework=net10.0
#:package GitHub.Copilot.SDK@1.0.4
using System;
using System.Linq;
using System.Reflection;

var asm = typeof(GitHub.Copilot.CopilotClient).Assembly;

Console.WriteLine("=== Types with 'State' or 'Connection' in name ===");
foreach (var t in asm.GetExportedTypes().Where(t => t.Name.Contains("State") || t.Name.Contains("Connection")).OrderBy(t => t.FullName))
{
    Console.WriteLine($"{t.FullName} (enum={t.IsEnum})");
    if (t.IsEnum) Console.WriteLine("   values: " + string.Join(", ", Enum.GetNames(t)));
}

var cc = typeof(GitHub.Copilot.CopilotClient);
Console.WriteLine("\n=== CopilotClient public instance properties ===");
foreach (var p in cc.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
    Console.WriteLine($"{p.PropertyType.Name} {p.Name}");
Console.WriteLine("\n=== CopilotClient public instance methods ===");
foreach (var m in cc.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName).Select(m => m.Name).Distinct().OrderBy(n => n))
    Console.WriteLine(m);

var sess = asm.GetType("GitHub.Copilot.CopilotSession");
if (sess != null)
{
    Console.WriteLine("\n=== CopilotSession public members ===");
    foreach (var p in sess.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
        Console.WriteLine($"prop: {p.PropertyType.Name} {p.Name}");
    foreach (var m in sess.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName).Select(m => m.Name).Distinct().OrderBy(n => n))
        Console.WriteLine($"method: {m}");
}
