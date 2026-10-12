using System.IO;
using System.Text;
using Mdict.Application;

internal static class LocalResourceTests
{
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MdictViewer-local-resources-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first");
            string second = Path.Combine(directory, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            string a = DictionaryFixture.Create(first, "LocalA", "red", "only-a");
            string b = DictionaryFixture.Create(second, "LocalB", "blue", "only-b");
            File.Delete(Path.ChangeExtension(b, ".mdd"));
            byte[] css = Encoding.UTF8.GetBytes(".marker { color: green; } /* 中文 */");
            File.WriteAllBytes(Path.Combine(first, "local.css"), css);
            File.WriteAllText(Path.Combine(second, "local.css"), "second dictionary");
            var service = new DictionaryService();
            service.Open(a);
            Check(service.GetResource("\\local.css")?.SequenceEqual(css) == true, "standalone CSS bytes load unchanged");
            var other = new DictionaryService();
            other.Open(b);
            Check(Encoding.UTF8.GetString(other.GetResource("local.css")) == "second dictionary", "resources stay in each MDX directory without requiring an MDD");
            Directory.CreateDirectory(Path.Combine(first, "assets"));
            byte[] binary = new byte[] { 0, 1, 128, 255 };
            foreach (string name in new[] { "icon.png", "font.woff", "script.js", "sound.mp3" })
                File.WriteAllBytes(Path.Combine(first, "assets", name), binary);
            Check(new[] { "assets/icon.png", "\\assets\\font.woff", "assets/script.js", "sound://assets/sound.mp3" }
                .All(p => service.GetResource(p)?.SequenceEqual(binary) == true), "nested image font script and audio paths");
            File.WriteAllText(Path.Combine(first, "shared.css"), "local override");
            Check(Encoding.UTF8.GetString(service.GetResource("shared.css")) == ".marker{color:red}", "existing MDD resource takes precedence over standalone file");
            File.WriteAllText(Path.Combine(directory, "outside.css"), "outside");
            Check(new[] { "../outside.css", ".. /outside.css", "assets/../../outside.css", "C:/outside.css", "local.css:stream", "local.css\0", "assets//icon.png" }
                .All(p => service.GetResource(p) == null), "unsafe local paths are rejected");
            Check(service.GetResource("missing.css") == null, "missing resource remains not found");
            other.Close();
            bool closedRejected = false;
            try { other.GetResource("local.css"); }
            catch (InvalidOperationException) { closedRejected = true; }
            Check(closedRejected, "closed service still rejects resource access");
            service.Close();
            Console.WriteLine("All 7 local resource tests passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("LOCAL RESOURCE TEST FAILED: " + ex.Message); return 1; }
        finally { Directory.Delete(directory, true); }
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new Exception(description);
        Console.WriteLine("PASS: " + description);
    }
}
