// KamiToolKit (latest) relocated several core types out of the root/`Premade`
// namespaces. These global usings keep the existing per-file using lists working
// after the migration without editing every Native window file:
//   - NativeAddon                -> KamiToolKit.Core.Primitives.NativeAddon
//   - NodeBase                   -> KamiToolKit.Core.Primitives.NodeBase
//   - ComponentNode              -> KamiToolKit.Core.Primitives.ComponentNode
//   - SimpleImageNode, SimpleNineGridNode -> KamiToolKit.UI.Nodes.Simplified
global using KamiToolKit.Core.Primitives.NativeAddon;
global using KamiToolKit.Core.Primitives.NodeBase;
global using KamiToolKit.Core.Primitives.ComponentNode;
global using KamiToolKit.UI.Nodes.Simplified;
