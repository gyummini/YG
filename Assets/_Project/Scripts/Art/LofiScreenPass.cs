using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace NightOffice
{
    /// <summary>
    /// Full-screen lo-fi pass after post-processing (the UI Toolkit overlay is drawn later, so it stays sharp).
    /// Enqueued per camera by <see cref="LofiPreview"/>; no renderer asset is modified.
    /// </summary>
    public class LofiScreenPass : ScriptableRenderPass
    {
        readonly Material m_Material;

        public LofiScreenPass(Material material)
        {
            m_Material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (m_Material == null || resources.isActiveTargetBackBuffer) return;
            var source = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "LofiScreen";
            desc.clearBuffer = false;
            var destination = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, m_Material, 0), "Lofi Screen");
            resources.cameraColor = destination;
        }
    }
}
