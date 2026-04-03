using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Echokraut.Services;

/// <summary>
/// Minimal Lua 5.1 bytecode parser that extracts Talk() call associations:
/// which actor variable calls Talk() with which text key string.
/// Also analyzes dispatch functions to map scene functions to ACTOR NPC IDs.
/// </summary>
public class LuabParser
{
    private readonly byte[] _data;
    private int _pos;

    // Header config
    private int _sizeInt;
    private int _sizeSizeT;
    private int _sizeInstruction;
    private int _sizeNumber;
    private bool _isLittleEndian;

    // Lua 5.1 opcodes
    private const int OP_LOADK = 1;
    private const int OP_GETUPVAL = 4;
    private const int OP_GETGLOBAL = 5;
    private const int OP_GETTABLE = 6;
    private const int OP_SETGLOBAL = 7;
    private const int OP_SETTABLE = 9;
    private const int OP_SELF = 11;
    private const int OP_EQ = 23;
    private const int OP_CALL = 28;
    private const int OP_CLOSURE = 36;

    private int _functionCounter;

    public LuabParser(byte[] data)
    {
        _data = data;
    }

    /// <summary>
    /// Parse the bytecode and return Talk() calls, dispatch-based scene→ACTOR mapping,
    /// and scene name→function index mapping from CLOSURE+SETGLOBAL analysis.
    /// </summary>
    public (List<TalkCall> talkCalls, Dictionary<int, string> sceneActorMap, Dictionary<string, int> sceneNameToFuncIndex) ParseWithDispatch()
    {
        _pos = 0;
        _functionCounter = 0;
        var talkCalls = new List<TalkCall>();
        var protos = new List<FunctionPrototype>();

        if (!ReadHeader())
            return (talkCalls, new Dictionary<int, string>(), new Dictionary<string, int>());

        ParseFunctionFull(talkCalls, "", null, protos, -1);

        var (sceneActorMap, sceneNameToFuncIndex) = BuildSceneActorMapWithNames(protos);
        return (talkCalls, sceneActorMap, sceneNameToFuncIndex);
    }

    public List<TalkCall> Parse()
    {
        _pos = 0;
        _functionCounter = 0;
        var result = new List<TalkCall>();

        if (!ReadHeader())
            return result;

        ParseFunctionFull(result, "", null, null, -1);
        return result;
    }

    public List<FunctionDebugData> ParseDebug()
    {
        _pos = 0;
        _functionCounter = 0;
        var functions = new List<FunctionDebugData>();

        if (!ReadHeader())
            return functions;

        ParseFunctionFull(new List<TalkCall>(), "", functions, null, -1);
        return functions;
    }

    private bool ReadHeader()
    {
        if (_data.Length < 12) return false;

        if (_data[0] != 0x1B || _data[1] != 0x4C || _data[2] != 0x75 || _data[3] != 0x61)
            return false;

        if (_data[4] != 0x51) return false;

        _isLittleEndian = _data[6] == 1;
        _sizeInt = _data[7];
        _sizeSizeT = _data[8];
        _sizeInstruction = _data[9];
        _sizeNumber = _data[10];

        _pos = 12;
        return true;
    }

    private void ParseFunctionFull(
        List<TalkCall> talkResult,
        string parentSource,
        List<FunctionDebugData>? debugOut,
        List<FunctionPrototype>? protoOut,
        int parentFuncIndex)
    {
        var funcIndex = _functionCounter++;

        var source = ReadString() ?? parentSource;

        ReadInt(); // linedefined
        ReadInt(); // lastlinedefined

        var nups = ReadByte();
        var numparams = ReadByte();
        var isVararg = ReadByte();
        var maxStackSize = ReadByte();

        var numInstructions = ReadInt();
        var instructions = new uint[numInstructions];
        for (var i = 0; i < numInstructions; i++)
            instructions[i] = ReadInstruction();

        var numConstants = ReadInt();
        var constants = new object?[numConstants];
        for (var i = 0; i < numConstants; i++)
        {
            var tag = ReadByte();
            switch (tag)
            {
                case 0: constants[i] = null; break;
                case 1: constants[i] = ReadByte() != 0; break;
                case 3: constants[i] = ReadNumber(); break;
                case 4: constants[i] = ReadString(); break;
                default: constants[i] = null; break;
            }
        }

        // Extract Talk() calls
        var pendingCalls = new List<TalkCall>();
        ExtractTalkCalls(instructions, constants, pendingCalls, source);

        // Record child prototype indices for this function
        var numProtos = ReadInt();
        var childFuncIndices = new List<int>();
        for (var i = 0; i < numProtos; i++)
        {
            childFuncIndices.Add(_functionCounter); // next function to be parsed = this child
            ParseFunctionFull(talkResult, source, debugOut, protoOut, funcIndex);
        }

        var locals = ParseDebugInfo();

        // Resolve register → parameter name
        foreach (var call in pendingCalls)
        {
            call.FunctionIndex = funcIndex;
            if (call.ActorRegister < locals.Count)
                call.ActorName = locals[call.ActorRegister].Name;
            talkResult.Add(call);
        }

        // Collect prototype metadata for dispatch analysis
        if (protoOut != null)
        {
            var stringConsts = new List<string>();
            for (var i = 0; i < constants.Length; i++)
                if (constants[i] is string s)
                    stringConsts.Add(s);

            protoOut.Add(new FunctionPrototype
            {
                FunctionIndex = funcIndex,
                ParentFunctionIndex = parentFuncIndex,
                NumParams = numparams,
                StringConstants = stringConsts.ToArray(),
                Instructions = instructions,
                AllConstants = constants,
                ChildFunctionIndices = childFuncIndices.ToArray(),
                HasTalkCalls = pendingCalls.Count > 0
            });
        }

        // Collect debug data
        if (debugOut != null)
        {
            var stringConstants = new List<string>();
            for (var i = 0; i < constants.Length; i++)
                if (constants[i] is string s)
                    stringConstants.Add($"[{i}] {s}");

            var hasTalk = constants.Any(c => c is string s && s.Equals("Talk", StringComparison.OrdinalIgnoreCase));
            var hasOnScene = constants.Any(c => c is string s && s.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase));
            var instructionDump = new List<string>();
            if (hasTalk || hasOnScene)
            {
                for (var i = 0; i < instructions.Length; i++)
                {
                    var inst = instructions[i];
                    var op = (int)(inst & 0x3F);
                    var a = (int)((inst >> 6) & 0xFF);
                    var bx = (int)((inst >> 14) & 0x3FFFF);
                    var b = (int)((inst >> 23) & 0x1FF);
                    var c = (int)((inst >> 14) & 0x1FF);

                    var cStr = (c & 256) != 0 && (c & 0xFF) < constants.Length && constants[c & 0xFF] is string cs
                        ? $" Kst({c & 0xFF})=\"{cs}\""
                        : "";
                    var bxStr = (op == OP_LOADK || op == OP_GETGLOBAL || op == OP_SETGLOBAL)
                        && bx < constants.Length && constants[bx] is string bs
                        ? $" Kst({bx})=\"{bs}\""
                        : "";

                    instructionDump.Add($"[{i:D3}] op={op:D2} A={a} B={b} C={c} Bx={bx}{cStr}{bxStr}");
                }
            }

            debugOut.Add(new FunctionDebugData
            {
                Source = source,
                NumParams = numparams,
                StringConstants = stringConstants,
                Locals = locals,
                TalkCalls = pendingCalls,
                InstructionDump = instructionDump
            });
        }
    }

    /// <summary>
    /// Build a mapping of function-index → ACTOR name by analyzing the dispatch function.
    /// Also returns scene name → function index mapping from CLOSURE+SETGLOBAL analysis.
    /// </summary>
    private (Dictionary<int, string> sceneActorMap, Dictionary<string, int> sceneNameToFuncIndex) BuildSceneActorMapWithNames(List<FunctionPrototype> protos)
    {
        var result = new Dictionary<int, string>();
        var globalNameToFuncIndex = new Dictionary<string, int>();
        if (protos.Count == 0) return (result, globalNameToFuncIndex);

        // Find the main chunk (function index 0)
        var mainChunk = protos.FirstOrDefault(p => p.FunctionIndex == 0);
        if (mainChunk == null) return (result, globalNameToFuncIndex);

        // Phase 1: Map OnSceneXXXXX names → function indices.
        // In FFXIV scripts, an init function (params=0) has OnScene names in its constants.
        // Scene functions are sibling children of the init function's parent (the module function).
        // The module function may be the main chunk itself or a child of it.
        var initFunc = protos.FirstOrDefault(p =>
            p.NumParams == 0
            && p.StringConstants.Any(c => c.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase)));

        if (initFunc != null)
        {
            // Pattern 1: CLOSURE+SETTABLE in init function.
            // Scene functions are child prototypes of the init function:
            //   CLOSURE R(1) = KPROTO[Bx]
            //   SETTABLE R(0)[RK(B)="OnSceneXXXXX"] = R(1)
            for (var i = 0; i < initFunc.Instructions.Length - 1; i++)
            {
                var inst = initFunc.Instructions[i];
                if ((inst & 0x3F) != OP_CLOSURE) continue;

                var closureBx = (int)((inst >> 14) & 0x3FFFF);

                var nextInst = initFunc.Instructions[i + 1];
                if ((nextInst & 0x3F) != OP_SETTABLE) continue;

                var stB = (int)((nextInst >> 23) & 0x1FF);
                if ((stB & 256) == 0) continue;
                var keyIdx = stB & 0xFF;
                if (keyIdx >= initFunc.AllConstants.Length) continue;
                if (initFunc.AllConstants[keyIdx] is not string keyName) continue;

                if (closureBx < initFunc.ChildFunctionIndices.Length)
                    globalNameToFuncIndex[keyName] = initFunc.ChildFunctionIndices[closureBx];
            }

            // Pattern 2: If SETTABLE didn't find OnScene mappings, try SETGLOBAL.
            // Some quests use CLOSURE+SETGLOBAL directly (no module table).
            if (!globalNameToFuncIndex.Keys.Any(k => k.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase)))
            {
                for (var i = 0; i < initFunc.Instructions.Length - 1; i++)
                {
                    var inst = initFunc.Instructions[i];
                    if ((inst & 0x3F) != OP_CLOSURE) continue;

                    var closureBx = (int)((inst >> 14) & 0x3FFFF);

                    var nextInst = initFunc.Instructions[i + 1];
                    if ((nextInst & 0x3F) != OP_SETGLOBAL) continue;

                    var setBx = (int)((nextInst >> 14) & 0x3FFFF);
                    if (setBx < initFunc.AllConstants.Length && initFunc.AllConstants[setBx] is string globalName)
                    {
                        if (closureBx < initFunc.ChildFunctionIndices.Length)
                            globalNameToFuncIndex[globalName] = initFunc.ChildFunctionIndices[closureBx];
                    }
                }
            }

            // Pattern 3: If still no OnScene mappings, scan ALL functions for CLOSURE+SETTABLE/SETGLOBAL.
            if (!globalNameToFuncIndex.Keys.Any(k => k.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var proto in protos)
                {
                    for (var i = 0; i < proto.Instructions.Length - 1; i++)
                    {
                        var inst = proto.Instructions[i];
                        if ((inst & 0x3F) != OP_CLOSURE) continue;

                        var closureBx = (int)((inst >> 14) & 0x3FFFF);

                        var nextInst = proto.Instructions[i + 1];
                        var nextOp = (int)(nextInst & 0x3F);
                        string? name = null;

                        if (nextOp == OP_SETTABLE)
                        {
                            var stB = (int)((nextInst >> 23) & 0x1FF);
                            if ((stB & 256) != 0)
                            {
                                var keyIdx = stB & 0xFF;
                                if (keyIdx < proto.AllConstants.Length && proto.AllConstants[keyIdx] is string k)
                                    name = k;
                            }
                        }
                        else if (nextOp == OP_SETGLOBAL)
                        {
                            var setBx = (int)((nextInst >> 14) & 0x3FFFF);
                            if (setBx < proto.AllConstants.Length && proto.AllConstants[setBx] is string g)
                                name = g;
                        }

                        if (name != null && name.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase)
                            && closureBx < proto.ChildFunctionIndices.Length)
                        {
                            globalNameToFuncIndex[name] = proto.ChildFunctionIndices[closureBx];
                        }
                    }
                }
            }
        }

        if (globalNameToFuncIndex.Count == 0) return (result, globalNameToFuncIndex);

        // Phase 2: Find the dispatch function (params=5, has SEQ_* and ACTOR* constants)
        var dispatchProto = protos.FirstOrDefault(p =>
            p.NumParams == 5
            && p.StringConstants.Any(c => c.StartsWith("SEQ_"))
            && p.StringConstants.Any(c => Regex.IsMatch(c, @"^ACTOR\d+$")));

        if (dispatchProto == null) return (result, globalNameToFuncIndex);

        // Phase 3: Analyze dispatch bytecode to map ACTOR → OnSceneXXXXX
        // The dispatch function uses EQ (op=23) to compare against ACTOR constants,
        // and GETGLOBAL (op=5) to reference OnSceneXXXXX functions.
        // Pattern: EQ test against ACTORX → eventually GETGLOBAL "OnSceneYYYYY" → CALL
        //
        // Simpler approach for FFXIV: the dispatch function returns data to native code
        // rather than calling scenes directly. The ACTOR constants appear in order,
        // and each ACTOR corresponds to scene functions in the same order.
        // We use the ordering of ACTOR references in the dispatch's EQ instructions.
        var actorOrder = ExtractActorOrderFromDispatch(dispatchProto);
        if (actorOrder.Count == 0) return (result, globalNameToFuncIndex);

        // Phase 4: Match ordered ACTORs to scene function prototypes
        // Scene functions are child prototypes with names matching OnSceneXXXXX.
        // They are registered in numerical order (OnScene00000, OnScene00001, ...).
        // Each ACTOR in the dispatch maps to one or more consecutive scene functions.
        // Single-speaker scenes (the ones we need to resolve) have exactly one ACTOR.
        var sceneFuncIndices = globalNameToFuncIndex
            .Where(kvp => kvp.Key.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => kvp.Value)
            .ToList();

        // Find scene functions that have Talk calls but weren't resolved (single-register)
        // For each such scene, find which ACTOR it belongs to by checking the dispatch
        // Each scene prototype that has Talk calls: find its OnSceneXXXXX name,
        // then find which ACTOR block in the dispatch references that scene.
        //
        // Since the dispatch doesn't always reference scene names directly,
        // we use a positional approach: ACTORs appear in the dispatch in the same order
        // as their scene functions appear in the prototype list.
        // Skip scene 0 (typically the quest offer/accept) and cutscene scenes.

        // First try direct: check if dispatch GETGLOBAL references OnSceneXXXXX
        var actorToScenes = ExtractActorToSceneNames(dispatchProto);
        if (actorToScenes.Count > 0)
        {
            // Direct mapping available
            foreach (var (actorName, sceneNames) in actorToScenes)
            {
                foreach (var sceneName in sceneNames)
                {
                    if (globalNameToFuncIndex.TryGetValue(sceneName, out var funcIdx))
                        result[funcIdx] = actorName;
                }
            }
            return (result, globalNameToFuncIndex);
        }

        // Fallback: positional matching
        // The dispatch EQ blocks reference ACTORs in order.
        // Scene functions are also in order.
        // Map: i-th ACTOR in dispatch → scene functions that have Talk calls with that ACTOR's index.
        // This is a heuristic but covers most FFXIV quests.
        var sceneProtos = protos
            .Where(p => p.ParentFunctionIndex == 0 && p.HasTalkCalls)
            .OrderBy(p => p.FunctionIndex)
            .ToList();

        // Build a lookup: funcIndex → all distinct Talk registers
        var funcRegisters = new Dictionary<int, HashSet<int>>();
        // We don't have talk calls in protos, but we have the funcIndex on each TalkCall
        // The caller (ParseWithDispatch) returns talkCalls — but we're inside BuildSceneActorMap
        // which only has protos. Let's use a different approach:
        // For single-speaker scenes, use the dispatch ACTOR order.

        // Map each scene function to its position among scene functions
        var singleSpeakerScenes = new List<int>(); // function indices of single-speaker scenes
        foreach (var sp in sceneProtos)
        {
            // Single-speaker: only one register used in Talk calls (we can infer from
            // the function having Talk calls but numparams <= 3)
            if (sp.NumParams <= 3)
                singleSpeakerScenes.Add(sp.FunctionIndex);
        }

        // The dispatch lists ACTORs in scene order. Each ACTOR maps to one scene function.
        // Skip ACTOR references that correspond to multi-speaker cutscene functions.
        // For simplicity, just map dispatch ACTOR[i] to single-speaker scene[i].
        // This works when each ACTOR has exactly one non-cutscene scene.

        // Better approach: ACTORs from dispatch, scenes from protos, match by position
        // among ALL scene functions (not just single-speaker).
        // Scene index 0 is often quest offer (no ACTOR), scene 1 is often accept cutscene.
        // The dispatch starts at SEQ_1, so we skip scenes before the first dispatched scene.

        // For now, just associate each ACTOR with all scenes that reference it
        // based on the dispatch ordering. This is best-effort.
        // The most reliable: each single-speaker scene function is called for exactly one ACTOR.
        // If we can match the count, we're good.

        return (result, globalNameToFuncIndex);
    }

    /// <summary>
    /// Extract ACTOR names in the order they appear in EQ comparisons in the dispatch function.
    /// </summary>
    private static List<string> ExtractActorOrderFromDispatch(FunctionPrototype dispatch)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();

        for (var i = 0; i < dispatch.Instructions.Length; i++)
        {
            var inst = dispatch.Instructions[i];
            var op = (int)(inst & 0x3F);

            // Look for EQ (op=23) or GETTABLE/GETGLOBAL that reference ACTOR constants
            if (op == OP_EQ)
            {
                // EQ A B C: skip if B or C don't reference an ACTOR constant
                var b = (int)((inst >> 23) & 0x1FF);
                var c = (int)((inst >> 14) & 0x1FF);

                string? actorName = null;
                if ((b & 256) != 0 && (b & 0xFF) < dispatch.AllConstants.Length
                    && dispatch.AllConstants[b & 0xFF] is string bs
                    && Regex.IsMatch(bs, @"^ACTOR\d+$"))
                    actorName = bs;
                else if ((c & 256) != 0 && (c & 0xFF) < dispatch.AllConstants.Length
                    && dispatch.AllConstants[c & 0xFF] is string cs
                    && Regex.IsMatch(cs, @"^ACTOR\d+$"))
                    actorName = cs;

                if (actorName != null && seen.Add(actorName))
                    result.Add(actorName);
            }
        }

        return result;
    }

    /// <summary>
    /// Try to extract direct ACTOR → OnSceneXXXXX mappings from the dispatch function.
    /// Looks for GETGLOBAL instructions that load OnSceneXXXXX names near ACTOR EQ blocks.
    /// Returns empty if the dispatch doesn't reference scene names directly.
    /// </summary>
    private static Dictionary<string, List<string>> ExtractActorToSceneNames(FunctionPrototype dispatch)
    {
        var result = new Dictionary<string, List<string>>();
        string? currentActor = null;

        for (var i = 0; i < dispatch.Instructions.Length; i++)
        {
            var inst = dispatch.Instructions[i];
            var op = (int)(inst & 0x3F);

            if (op == OP_EQ)
            {
                var b = (int)((inst >> 23) & 0x1FF);
                var c = (int)((inst >> 14) & 0x1FF);

                // Check if this EQ references an ACTOR constant
                if ((b & 256) != 0 && (b & 0xFF) < dispatch.AllConstants.Length
                    && dispatch.AllConstants[b & 0xFF] is string bs
                    && Regex.IsMatch(bs, @"^ACTOR\d+$"))
                    currentActor = bs;
                else if ((c & 256) != 0 && (c & 0xFF) < dispatch.AllConstants.Length
                    && dispatch.AllConstants[c & 0xFF] is string cs
                    && Regex.IsMatch(cs, @"^ACTOR\d+$"))
                    currentActor = cs;
            }
            else if (op == OP_GETGLOBAL && currentActor != null)
            {
                var bx = (int)((inst >> 14) & 0x3FFFF);
                if (bx < dispatch.AllConstants.Length
                    && dispatch.AllConstants[bx] is string globalName
                    && globalName.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase))
                {
                    if (!result.TryGetValue(currentActor, out var scenes))
                    {
                        scenes = new List<string>();
                        result[currentActor] = scenes;
                    }
                    if (!scenes.Contains(globalName))
                        scenes.Add(globalName);
                }
            }
        }

        return result;
    }

    private void ExtractTalkCalls(uint[] instructions, object?[] constants, List<TalkCall> result, string source)
    {
        for (var i = 0; i < instructions.Length; i++)
        {
            var inst = instructions[i];
            var opcode = inst & 0x3F;

            if (opcode != OP_SELF) continue;

            var a = (int)((inst >> 6) & 0xFF);
            var b = (int)((inst >> 23) & 0x1FF);
            var c = (int)((inst >> 14) & 0x1FF);

            if ((c & 256) == 0) continue;
            var cIdx = c & 0xFF;
            if (cIdx >= constants.Length || constants[cIdx] is not string methodName) continue;
            if (!methodName.Equals("Talk", StringComparison.OrdinalIgnoreCase)) continue;

            for (var j = i + 1; j < Math.Min(i + 10, instructions.Length); j++)
            {
                var nextInst = instructions[j];
                var nextOp = nextInst & 0x3F;

                if (nextOp == OP_CALL) break;

                string? textKey = null;

                if (nextOp == OP_LOADK)
                {
                    var loadkBx = (int)((nextInst >> 14) & 0x3FFFF);
                    if (loadkBx < constants.Length && constants[loadkBx] is string s)
                        textKey = s;
                }
                else if (nextOp == OP_GETTABLE)
                {
                    var gtC = (int)((nextInst >> 14) & 0x1FF);
                    if ((gtC & 256) != 0)
                    {
                        var gtIdx = gtC & 0xFF;
                        if (gtIdx < constants.Length && constants[gtIdx] is string s)
                            textKey = s;
                    }
                }

                if (textKey != null && textKey.StartsWith("TEXT_", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new TalkCall
                    {
                        ActorRegister = b,
                        TextKey = textKey,
                        Source = source
                    });
                }
            }
        }
    }

    private List<LocalVar> ParseDebugInfo()
    {
        var n = ReadInt();
        _pos += n * _sizeInt;

        var locals = new List<LocalVar>();
        n = ReadInt();
        for (var i = 0; i < n; i++)
        {
            var name = ReadString() ?? "";
            var startpc = ReadInt();
            var endpc = ReadInt();
            locals.Add(new LocalVar { Name = name, StartPc = startpc, EndPc = endpc });
        }

        n = ReadInt();
        for (var i = 0; i < n; i++)
            ReadString();

        return locals;
    }

    private int ReadInt()
    {
        var val = _isLittleEndian
            ? BitConverter.ToInt32(_data, _pos)
            : BitConverter.ToInt32(new[] { _data[_pos + 3], _data[_pos + 2], _data[_pos + 1], _data[_pos] }, 0);
        _pos += _sizeInt;
        return val;
    }

    private uint ReadInstruction()
    {
        var val = _isLittleEndian
            ? BitConverter.ToUInt32(_data, _pos)
            : BitConverter.ToUInt32(new[] { _data[_pos + 3], _data[_pos + 2], _data[_pos + 1], _data[_pos] }, 0);
        _pos += _sizeInstruction;
        return val;
    }

    private double ReadNumber()
    {
        var val = BitConverter.ToDouble(_data, _pos);
        _pos += _sizeNumber;
        return val;
    }

    private byte ReadByte()
    {
        return _data[_pos++];
    }

    private string? ReadString()
    {
        var size = _sizeSizeT == 4
            ? (int)(_isLittleEndian ? BitConverter.ToUInt32(_data, _pos) : BitConverter.ToUInt32(new[] { _data[_pos + 3], _data[_pos + 2], _data[_pos + 1], _data[_pos] }, 0))
            : (int)(_isLittleEndian ? BitConverter.ToUInt64(_data, _pos) : 0);
        _pos += _sizeSizeT;

        if (size == 0) return null;

        var str = Encoding.UTF8.GetString(_data, _pos, size - 1);
        _pos += size;
        return str;
    }

    // ── Public data classes ──────────────────────────────────────────────

    public class TalkCall
    {
        public int ActorRegister { get; set; }
        public string TextKey { get; set; } = "";
        public string Source { get; set; } = "";
        public string? ActorName { get; set; }
        public int FunctionIndex { get; set; }
    }

    internal class LocalVar
    {
        public string Name { get; set; } = "";
        public int StartPc { get; set; }
        public int EndPc { get; set; }
    }

    internal class FunctionPrototype
    {
        public int FunctionIndex { get; set; }
        public int ParentFunctionIndex { get; set; }
        public int NumParams { get; set; }
        public string[] StringConstants { get; set; } = Array.Empty<string>();
        public uint[] Instructions { get; set; } = Array.Empty<uint>();
        public object?[] AllConstants { get; set; } = Array.Empty<object?>();
        public int[] ChildFunctionIndices { get; set; } = Array.Empty<int>();
        public bool HasTalkCalls { get; set; }
    }

    public class FunctionDebugData
    {
        public string Source { get; set; } = "";
        public int NumParams { get; set; }
        public List<string> StringConstants { get; set; } = new();
        internal List<LocalVar> Locals { get; set; } = new();
        internal List<TalkCall> TalkCalls { get; set; } = new();
        public List<string> InstructionDump { get; set; } = new();

        public List<string> LocalNames => Locals.Select(l => $"{l.Name} (pc {l.StartPc}-{l.EndPc})").ToList();
        public List<string> TalkCallSummaries => TalkCalls.Select(t =>
            $"reg={t.ActorRegister} actorName={t.ActorName ?? "?"} textKey={t.TextKey}").ToList();
    }
}
