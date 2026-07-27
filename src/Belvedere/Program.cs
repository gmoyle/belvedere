using Belvedere.Models;
using Belvedere.Services;
using Belvedere.UI;

namespace Belvedere;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single instance: if Belvedere is already running, just exit.
        using var mutex = new Mutex(initiallyOwned: true, "Belvedere.SingleInstance.9F2A", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Belvedere is already running (look in the system tray).",
                "Belvedere", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var store = new ConfigStore();
        var log = new Logger(Path.Combine(store.DataFolder, "event.log"));

        bool firstRun = !File.Exists(store.ConfigPath);
        var loadResult = store.Load();
        var config = loadResult.Config;

        if (loadResult.Warning is not null)
        {
            MessageBox.Show(loadResult.Warning, "Belvedere settings reset",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (firstRun)
            TryFirstRunImport(store, config);

        log.Enabled = config.EnableLogging;
        log.Info($"Starting Belvedere {typeof(Program).Assembly.GetName().Version?.ToString(3)}");

        Application.ThreadException += (_, e) => log.Error("UI exception: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("Fatal: " + e.ExceptionObject);

        Application.Run(new TrayAppContext(store, log, config));
    }

    /// <summary>
    /// On first launch, offer to import a legacy rules.ini found next to the
    /// old app (or wherever the user points us), so upgraders keep their rules.
    /// </summary>
    private static void TryFirstRunImport(ConfigStore store, AppConfig config)
    {
        // Look in a few likely spots for a legacy config.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "rules.ini"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules.ini"),
        };
        string? found = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);

        var prompt = found is not null
            ? $"Welcome to Belvedere 2.0!\n\nA legacy rules.ini was found at:\n{found}\n\nImport your existing rules?"
            : "Welcome to Belvedere 2.0!\n\nDo you have a rules.ini from the old version you'd like to import?";

        var choice = MessageBox.Show(prompt, "Import existing rules?",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (choice != DialogResult.Yes) { store.Save(config); return; }

        string? path = found;
        if (path is null)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select your legacy rules.ini",
                Filter = "Belvedere settings (*.ini)|*.ini|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() != DialogResult.OK) { store.Save(config); return; }
            path = dlg.FileName;
        }

        try
        {
            var imported = IniImporter.Import(path);
            config.Rules.AddRange(imported.Config.Rules);
            config.SweepInterval = imported.Config.SweepInterval;
            config.SweepUnit = imported.Config.SweepUnit;
            config.EnableLogging = imported.Config.EnableLogging;
            config.ConfirmExit = imported.Config.ConfirmExit;
            MessageBox.Show(IniImporter.FormatSummary(imported) + "\n\nYou can review them under Manage.",
                "Import complete", MessageBoxButtons.OK,
                imported.Warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not import that file:\n{ex.Message}", "Import error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        store.Save(config);
    }
}
