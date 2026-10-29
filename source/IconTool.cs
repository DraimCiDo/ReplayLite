namespace ReplayLite
{
    static class IconTool
    {
        static int Main(string[] args) { Theme.SetPalette("mint"); AppIcons.Export(args[0]); return 0; }
    }
}
