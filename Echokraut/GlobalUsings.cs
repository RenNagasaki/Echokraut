// KamiToolKit (latest) relocated several core types out of the root/`Premade`
// namespaces. These global usings keep the existing per-file using lists working
// after the migration without editing every Native window file:
//   - NativeAddon, NodeBase, ComponentNode  -> KamiToolKit.BaseTypes
//   - SimpleImageNode, SimpleNineGridNode    -> KamiToolKit.Nodes.Simplified
global using KamiToolKit.BaseTypes;
global using KamiToolKit.Nodes.Simplified;
