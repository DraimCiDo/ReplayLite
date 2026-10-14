using System;
using System.IO;
using System.Reflection;
using System.Threading;
#if NEXT
[assembly: AssemblyVersion("2.0.0.0")]
#else
[assembly: AssemblyVersion("1.0.0.0")]
#endif
static class UpdateFixture
{
    static void Main()
    {
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launch.txt"), Assembly.GetExecutingAssembly().GetName().Version.ToString());
#if !NEXT
        Thread.Sleep(3000);
#endif
    }
}
