using System;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Shared builder for Alltalk instance UI sections used by both NativeConfigWindow and NativeFirstTimeWindow.
/// Ensures the install process, local instance controls, and remote instance controls are always identical.
/// </summary>
public static class NativeAlltalkBuilder
{
    /// <summary>All nodes created for a local instance section.</summary>
    public class LocalInstanceNodes
    {
        public TextInputNode InstallPathInput = null!;
        public CheckboxNode IsWindows11Check = null!;
        public TextInputNode CustomModelUrlInput = null!;
        public TextInputNode CustomVoicesUrlInput = null!;
        public TextButtonNode InstallCustomDataButton = null!;
        public CheckboxNode AutoStartCheck = null!;
        public TextButtonNode InstallButton = null!;
        public TextButtonNode StartButton = null!;
        public TextButtonNode StopButton = null!;
        public HorizontalListNode StartStopRow = null!;

        /// <summary>All nodes in display order, for adding to a list or collapsible section.</summary>
        public NodeBase[] AllNodes => [
            InstallPathInput, IsWindows11Check,
            CustomModelUrlInput, CustomVoicesUrlInput, InstallCustomDataButton,
            AutoStartCheck, InstallButton, StartStopRow,
        ];

        /// <summary>Updates button labels and dimming each frame.</summary>
        public void Update(Configuration config, IAlltalkInstanceService alltalkInstance)
        {
            // Install button
            InstallButton.String = alltalkInstance.Installing
                ? "Installing..."
                : config.Alltalk.LocalInstall ? "Reinstall" : "Install";
            Dim(InstallButton, !alltalkInstance.Installing);

            // Install custom data — only when installed and not currently installing
            Dim(InstallCustomDataButton, config.Alltalk.LocalInstall && !alltalkInstance.Installing);

            // Start/Stop
            StartButton.String = alltalkInstance.InstanceStarting ? "Starting..."
                : alltalkInstance.InstanceRunning ? "Running" : "Start";
            Dim(StartButton, !alltalkInstance.InstanceRunning && !alltalkInstance.InstanceStarting);
            Dim(StopButton, (alltalkInstance.InstanceRunning || alltalkInstance.InstanceStarting) && !alltalkInstance.InstanceStopping);
        }
    }

    /// <summary>All nodes created for a remote instance section.</summary>
    public class RemoteInstanceNodes
    {
        public TextInputNode BaseUrlInput = null!;
        public TextButtonNode TestConnectionButton = null!;
        public TextNode ConnectionResultLabel = null!;

        public NodeBase[] AllNodes => [BaseUrlInput, TestConnectionButton, ConnectionResultLabel];
    }

    /// <summary>Creates the local instance UI nodes. Order matches the ImGui version.</summary>
    public static LocalInstanceNodes BuildLocalInstance(float width, Configuration config, IAlltalkInstanceService alltalkInstance)
    {
        var nodes = new LocalInstanceNodes();

        // Install path
        nodes.InstallPathInput = MakeInput("Local install path (no spaces or dashes)", width, 128,
            config.Alltalk.LocalInstallPath,
            v => { config.Alltalk.LocalInstallPath = v; config.Save(); });

        // Is Windows 11
        nodes.IsWindows11Check = MakeCheck("Is Windows 11", width, config.Alltalk.IsWindows11,
            v => { config.Alltalk.IsWindows11 = v; config.Save(); });

        // Custom model URL
        nodes.CustomModelUrlInput = MakeInput("Custom model URL (zip with one root folder)", width, 256,
            config.Alltalk.CustomModelUrl,
            v => { config.Alltalk.CustomModelUrl = v; config.Save(); });

        // Custom voices URL
        nodes.CustomVoicesUrlInput = MakeInput("Custom voices URL (zip with \"voices\" folder)", width, 256,
            config.Alltalk.CustomVoicesUrl,
            v => { config.Alltalk.CustomVoicesUrl = v; config.Save(); });

        // Install only custom data
        nodes.InstallCustomDataButton = MakeButton("Install only custom data", 200, () =>
            alltalkInstance.InstallCustomData(new EKEventId(0, TextSource.Backend), false));

        // Auto-start
        nodes.AutoStartCheck = MakeCheck("Auto start local instance on plugin load", width,
            config.Alltalk.AutoStartLocalInstance,
            v =>
            {
                config.Alltalk.AutoStartLocalInstance = v;
                config.Save();
                if (v && config.Alltalk.LocalInstall && !alltalkInstance.InstanceRunning && !alltalkInstance.InstanceStarting)
                    alltalkInstance.StartInstance();
            });

        // Install/Reinstall
        nodes.InstallButton = MakeButton(config.Alltalk.LocalInstall ? "Reinstall" : "Install", 200, () =>
        {
            if (alltalkInstance.InstanceRunning || alltalkInstance.InstanceStarting)
                alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend));
            alltalkInstance.Install();
        });

        // Start/Stop row
        nodes.StartButton = MakeButton("Start", 80, () => alltalkInstance.StartInstance());
        nodes.StopButton = MakeButton("Stop", 80, () => alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend)));
        nodes.StartStopRow = new HorizontalListNode { Size = new Vector2(width, 26), ItemSpacing = 4 };
        nodes.StartStopRow.AddNode(nodes.StartButton);
        nodes.StartStopRow.AddNode(nodes.StopButton);

        return nodes;
    }

    /// <summary>Creates the remote instance UI nodes.</summary>
    public static RemoteInstanceNodes BuildRemoteInstance(float width, Configuration config, IBackendService backend)
    {
        var nodes = new RemoteInstanceNodes();

        nodes.BaseUrlInput = MakeInput("Alltalk base URL", width, 80, config.Alltalk.BaseUrl,
            v => { config.Alltalk.BaseUrl = v; config.Save(); });

        nodes.TestConnectionButton = MakeButton("Test", 60, () => { });
        nodes.ConnectionResultLabel = new TextNode
        {
            Size = new Vector2(width, 20),
            String = " ",
            FontType = FontType.Axis,
            FontSize = 12,
        };

        return nodes;
    }

    // ── Private helpers (mirror NativeConfigWindow's factory methods) ─────────

    private static void Dim(NodeBase? node, bool enabled)
    {
        if (node != null) node.Alpha = enabled ? 1.0f : 0.4f;
    }

    private static CheckboxNode MakeCheck(string label, float width, bool initial, Action<bool> onChange) => new()
    {
        Size = new Vector2(width, 24),
        String = label,
        IsChecked = initial,
        OnClick = onChange,
    };

    private static TextButtonNode MakeButton(string label, float width, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(width, 24), String = label };
        node.OnClick = onClick;
        return node;
    }

    private static TextInputNode MakeInput(string placeholder, float width, int maxChars, string initial, Action<string> onChanged)
    {
        var node = new TextInputNode
        {
            Size = new Vector2(width, 28),
            MaxCharacters = maxChars,
            PlaceholderString = placeholder,
            String = initial,
        };
        node.OnInputReceived = s => onChanged(s.ToString());
        return node;
    }
}
