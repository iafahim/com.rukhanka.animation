using System;
using System.IO;
using Rukhanka.WaybackMachine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
public static class WaybackMachineDataExtensions
{
    public static unsafe void SerializeToFile(this WaybackMachineData wmd, string path)
    {
        using var f = File.Open(path, FileMode.Create);
        using var bw = new BinaryWriter(f);
        bw.Write(WaybackMachineWindow.BINARY_MAGIC);
        bw.Write(WaybackMachineWindow.BINARY_VERSION);
        
        bw.Write((int)wmd.fpsMode);
        bw.Write(wmd.lastRecordedFrame);
        bw.Write(wmd.animHistory.Length);
        foreach (var ah in wmd.animHistory)
        {
            bw.Write(ah.frameSpan.x);
            bw.Write(ah.frameSpan.y);
            
            bw.Write(ah.animationHash.Value.x);
            bw.Write(ah.animationHash.Value.y);
            bw.Write(ah.animationHash.Value.z);
            bw.Write(ah.animationHash.Value.w);
            
            bw.Write(ah.avatarMaskHash.Value.x);
            bw.Write(ah.avatarMaskHash.Value.y);
            bw.Write(ah.avatarMaskHash.Value.z);
            bw.Write(ah.avatarMaskHash.Value.w);
            
            bw.Write((int)ah.blendMode);
            bw.Write(ah.layerWeight);
            bw.Write(ah.layerIndex);
            bw.Write(ah.motionId);
            
            bw.Write(ah.historyWeights.Length);
            var weightsHistorySpan = new ReadOnlySpan<byte>(ah.historyWeights.Ptr, ah.historyWeights.Length * UnsafeUtility.SizeOf<HistoryValue>());
            bw.Write(weightsHistorySpan);
            
            bw.Write(ah.historyAnimTime.Length);
            var timesHistorySpan = new ReadOnlySpan<byte>(ah.historyAnimTime.Ptr, ah.historyAnimTime.Length * UnsafeUtility.SizeOf<HistoryValue>());
            bw.Write(timesHistorySpan);
            
            bw.Write(ah.animationName.Length);
            var nameSpan = new ReadOnlySpan<byte>(ah.animationName.GetUnsafePtr(), ah.animationName.Length);
            bw.Write(nameSpan);
        }
        
        bw.Write(wmd.controllerStateHistory.Length);
        var stateHistoryDataSpan = new ReadOnlySpan<byte>(wmd.controllerStateHistory.GetUnsafePtr(),
            wmd.controllerStateHistory.Length * UnsafeUtility.SizeOf<AnimatorControllerStateHistoryData>());
        bw.Write(stateHistoryDataSpan);
        
        bw.Write(wmd.controllerTransitionHistory.Length);
        var transitionHistoryDataSpan = new ReadOnlySpan<byte>(wmd.controllerTransitionHistory.GetUnsafePtr(),
            wmd.controllerTransitionHistory.Length * UnsafeUtility.SizeOf<AnimatorControllerTransitionHistoryData>());
        bw.Write(transitionHistoryDataSpan);
        
        bw.Write(wmd.animationEventHistory.Length);
        var animationEventHistoryDataSpan = new ReadOnlySpan<byte>(wmd.animationEventHistory.GetUnsafePtr(),
            wmd.animationEventHistory.Length * UnsafeUtility.SizeOf<AnimationEventHistoryData>());
        bw.Write(animationEventHistoryDataSpan);
        
        bw.Write(wmd.animatorEventHistory.Length);
        var animatorEventHistoryDataSpan = new ReadOnlySpan<byte>(wmd.animatorEventHistory.GetUnsafePtr(),
            wmd.animatorEventHistory.Length * UnsafeUtility.SizeOf<AnimatorEventHistoryData>());
        bw.Write(animatorEventHistoryDataSpan);
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static unsafe void SerializeFromFile(this ref WaybackMachineData wmd, string path)
    {
        using var f = File.Open(path, FileMode.Open);
        using var br = new BinaryReader(f);
        var binaryMagic = br.ReadUInt32();
        if (binaryMagic != WaybackMachineWindow.BINARY_MAGIC)
        {
            EditorUtility.DisplayDialog("Rukhanka Wayback Machine", "Recorded animation data file is corrupted.", "Close");
            return;
        }
        var binaryVersion = br.ReadInt32();
        if (binaryVersion != WaybackMachineWindow.BINARY_VERSION)
        {
            EditorUtility.DisplayDialog("Rukhanka Wayback Machine", "Recorded animation data was made with previous version of wayback machine. Cannot load.", "Close");
            return;
        }
        
        wmd.Clear();
        
        wmd.fpsMode = (WaybackMachineData.FPSMode)br.ReadInt32();
        wmd.lastRecordedFrame = br.ReadInt32();
        var animHistoryLen = br.ReadInt32();
        for (var i = 0; i < animHistoryLen; ++i)
        {
            var ah = new AnimationHistoryData();
            ah.frameSpan.x = br.ReadInt32();
            ah.frameSpan.y = br.ReadInt32();
            
            ah.animationHash.Value.x = br.ReadUInt32();
            ah.animationHash.Value.y = br.ReadUInt32();
            ah.animationHash.Value.z = br.ReadUInt32();
            ah.animationHash.Value.w = br.ReadUInt32();
            
            ah.avatarMaskHash.Value.x = br.ReadUInt32();
            ah.avatarMaskHash.Value.y = br.ReadUInt32();
            ah.avatarMaskHash.Value.z = br.ReadUInt32();
            ah.avatarMaskHash.Value.w = br.ReadUInt32();
            
            ah.blendMode = (AnimationBlendingMode)br.ReadInt32();
            ah.layerWeight = br.ReadSingle();
            ah.layerIndex = br.ReadInt32();
            ah.motionId = br.ReadUInt32();
            
            var weightHistoryLen = br.ReadInt32();
            ah.historyWeights = new (weightHistoryLen, Allocator.Persistent);
            ah.historyWeights.Resize(weightHistoryLen);
            var weightsHistorySpan = new Span<byte>(ah.historyWeights.Ptr, ah.historyWeights.Length * UnsafeUtility.SizeOf<HistoryValue>());
            br.Read(weightsHistorySpan);
            
            var timesHistoryLen = br.ReadInt32();
            ah.historyAnimTime = new (timesHistoryLen, Allocator.Persistent);
            ah.historyAnimTime.Resize(timesHistoryLen, NativeArrayOptions.ClearMemory);
            var timesHistorySpan = new Span<byte>(ah.historyAnimTime.Ptr, ah.historyAnimTime.Length * UnsafeUtility.SizeOf<HistoryValue>());
            br.Read(timesHistorySpan);
            
            var nameLen = br.ReadInt32();
            ah.animationName.TryResize(nameLen);
            var nameSpan = new Span<byte>(ah.animationName.GetUnsafePtr(), ah.animationName.Length);
            br.Read(nameSpan);
            
            wmd.animHistory.Add(ah);
        }
        
        var stateHistoryLen = br.ReadInt32();
        wmd.controllerStateHistory.Resize(stateHistoryLen, NativeArrayOptions.ClearMemory);
        var stateHistoryDataSpan = new Span<byte>(wmd.controllerStateHistory.GetUnsafePtr(),
            wmd.controllerStateHistory.Length * UnsafeUtility.SizeOf<AnimatorControllerStateHistoryData>());
        br.Read(stateHistoryDataSpan);
        
        var transitionHistoryLen = br.ReadInt32();
        wmd.controllerTransitionHistory.Resize(transitionHistoryLen, NativeArrayOptions.ClearMemory);
        var transitionHistoryDataSpan = new Span<byte>(wmd.controllerTransitionHistory.GetUnsafePtr(),
            wmd.controllerTransitionHistory.Length * UnsafeUtility.SizeOf<AnimatorControllerTransitionHistoryData>());
        br.Read(transitionHistoryDataSpan);
        
        var animationEventHistoryLen = br.ReadInt32();
        wmd.animationEventHistory.Length = animationEventHistoryLen;
        var animationEventHistoryDataSpan = new Span<byte>(wmd.animationEventHistory.GetUnsafePtr(),
            wmd.animationEventHistory.Length * UnsafeUtility.SizeOf<AnimationEventHistoryData>());
        br.Read(animationEventHistoryDataSpan);
        
        var animatorEventHistoryLen = br.ReadInt32();
        wmd.animatorEventHistory.Length = animatorEventHistoryLen;
        var animatorEventHistoryDataSpan = new Span<byte>(wmd.animatorEventHistory.GetUnsafePtr(),
            wmd.animatorEventHistory.Length * UnsafeUtility.SizeOf<AnimatorEventHistoryData>());
        br.Read(animatorEventHistoryDataSpan);
    }
}
}
