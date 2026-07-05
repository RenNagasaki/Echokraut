using System;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

using static Echokraut.Windows.Native.NativeNodeFactory;
namespace Echokraut.Windows.Native;

/// <summary>
/// Shared builder for the EchokrauTTS instance UI sections (Backend tab + First-Time wizard),
/// parallel to <see cref="NativeAlltalkBuilder"/> but simpler: the wrapper self-bootstraps, so
/// there's no CPU-mode / Windows-11 / custom-model / install-custom-data plumbing. The install
/// path is the SHARED <c>Configuration.TtsInstallRoot</c> (same field AllTalk edits).
/// </summary>
public static class NativeEchokrauTtsBuilder
{
    /// <summary>All nodes created for the EchokrauTTS local instance section.</summary>
    public class LocalInstanceNodes
    {
        public TextInputNode InstallPathInput = null!;
        public TextNode ValidationLabel = null!;
        public CheckboxNode AutoStartCheck = null!;
        public TextButtonNode InstallButton = null!;
        public HorizontalListNode InstallRow = null!;
        public TextButtonNode StartButton = null!;
        public TextButtonNode StopButton = null!;
        public HorizontalListNode StartStopRow = null!;

        public NodeBase[] AllNodes => [InstallPathInput, ValidationLabel, AutoStartCheck, InstallRow, StartStopRow];

        public void Update(Configuration config, IEchokrauTtsInstanceService instance, bool batchActive = false)
        {
            var (pathValid, validationMsg) = NativeAlltalkBuilder.ValidateInstallPath(config.TtsInstallRoot);
            ValidationLabel.IsVisible = !pathValid;
            if (!pathValid) ValidationLabel.String = validationMsg;

            InstallButton.String = InstallLabel(instance, config);
            Dim(InstallButton, pathValid && !instance.Installing && !batchActive);

            StartButton.String = StartLabel(instance);
            Dim(StartButton, pathValid
                && !instance.InstanceRunning && !instance.InstanceStarting
                && !instance.Installing && !batchActive);
            Dim(StopButton, (instance.InstanceRunning || instance.InstanceStarting) && !instance.InstanceStopping);
        }

        private static string InstallLabel(IEchokrauTtsInstanceService instance, Configuration config)
        {
            if (instance.Installing) return Loc.S("Installing...");
            return config.EchokrauTts.LocalInstall ? Loc.S("Reinstall") : Loc.S("Install");
        }

        private static string StartLabel(IEchokrauTtsInstanceService instance)
        {
            if (instance.InstanceStarting) return Loc.S("Starting...");
            return instance.InstanceRunning ? Loc.S("Running") : Loc.S("Start");
        }
    }

    /// <summary>All nodes created for the EchokrauTTS remote instance section.</summary>
    public class RemoteInstanceNodes
    {
        public TextInputNode BaseUrlInput = null!;
        public TextButtonNode TestConnectionButton = null!;
        public TextNode ConnectionResultLabel = null!;

        public NodeBase[] AllNodes => [BaseUrlInput, TestConnectionButton, ConnectionResultLabel];
    }

    public static LocalInstanceNodes BuildLocalInstance(float width, Configuration config, IEchokrauTtsInstanceService instance)
    {
        var nodes = new LocalInstanceNodes();

        if (string.IsNullOrWhiteSpace(config.TtsInstallRoot))
        {
            config.TtsInstallRoot = Configuration.DefaultTtsInstallRoot;
            config.Save();
        }

        nodes.InstallPathInput = Input(Loc.S("Local install path (no spaces or dashes)"), width, 128,
            config.TtsInstallRoot,
            v => { config.TtsInstallRoot = v; config.Save(); });

        nodes.ValidationLabel = new TextNode
        {
            Size = new Vector2(width, 18),
            String = Loc.S("The Alltalk path must not be empty.\r\nPlease enter a valid path."),
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = new Vector4(1f, 0.3f, 0.3f, 1f),
            IsVisible = string.IsNullOrWhiteSpace(config.TtsInstallRoot),
        };

        nodes.AutoStartCheck = Check(Loc.S("Auto-start local instance on plugin load"), width,
            config.EchokrauTts.AutoStartLocalInstance,
            v =>
            {
                config.EchokrauTts.AutoStartLocalInstance = v;
                config.Save();
                if (v && config.EchokrauTts.LocalInstall && !instance.InstanceRunning && !instance.InstanceStarting)
                    instance.StartInstance();
            });

        nodes.InstallButton = Button(config.EchokrauTts.LocalInstall ? Loc.S("Reinstall") : Loc.S("Install"), 100, () =>
        {
            if (instance.InstanceRunning || instance.InstanceStarting)
                instance.StopInstance(new EKEventId(0, TextSource.Backend));
            instance.Install();
        });
        var installMaxW = new[] { Loc.S("Install"), Loc.S("Reinstall"), Loc.S("Installing...") }
            .Max(s => nodes.InstallButton.LabelNode.GetTextDrawSize(s).X) + 36;
        if (installMaxW > nodes.InstallButton.Width)
            nodes.InstallButton.Size = new Vector2(installMaxW, 24);
        nodes.InstallRow = new HorizontalListNode { Size = new Vector2(width, 26), ItemSpacing = 4 };
        nodes.InstallRow.AddNode(nodes.InstallButton);

        nodes.StartButton = Button(Loc.S("Start"), 80, () => instance.StartInstance());
        var startMaxW = new[] { Loc.S("Start"), Loc.S("Starting..."), Loc.S("Running") }
            .Max(s => nodes.StartButton.LabelNode.GetTextDrawSize(s).X) + 36;
        if (startMaxW > nodes.StartButton.Width)
            nodes.StartButton.Size = new Vector2(startMaxW, 24);
        nodes.StopButton = Button(Loc.S("Stop"), 80, () => instance.StopInstance(new EKEventId(0, TextSource.Backend)));
        nodes.StartStopRow = new HorizontalListNode { Size = new Vector2(width, 26), ItemSpacing = 4 };
        nodes.StartStopRow.AddNode(nodes.StartButton);
        nodes.StartStopRow.AddNode(nodes.StopButton);

        return nodes;
    }

    public static RemoteInstanceNodes BuildRemoteInstance(float width, Configuration config)
    {
        var nodes = new RemoteInstanceNodes();

        nodes.BaseUrlInput = Input(Loc.S("EchokrauTTS base URL"), width, 80, config.EchokrauTts.BaseUrl,
            v => { config.EchokrauTts.BaseUrl = v; config.Save(); });

        nodes.TestConnectionButton = Button(Loc.S("Test"), 60, () => { });
        nodes.ConnectionResultLabel = new TextNode
        {
            Size = new Vector2(width, 20),
            String = " ",
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = LabelColor,
        };

        return nodes;
    }

}
