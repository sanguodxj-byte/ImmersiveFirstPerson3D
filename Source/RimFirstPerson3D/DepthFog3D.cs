using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using HarmonyLib;
using RimWorld.Planet;
using RimWorldWaylith.Rendering;
using UnityEngine;
using Verse;
using Waylith;
using Waylith.Graphics;
using Waylith.Rendering;
using Waylith.Rendering.Compute;
using Waylith.Rendering.PostFx;
using RenderTexture = Waylith.Rendering.RenderTexture;

namespace RimFirstPerson3D
{
    public static class DepthFog3D
    {
        public sealed class FogPayload
        {
            public readonly Vector4 Right;
            public readonly Vector4 Up;
            public readonly Vector4 Forward;
            public readonly Vector4 Atmosphere;
            public readonly Vector4 SunDir;
            public readonly Vector4 Horizon;
            public readonly Vector4 Sun;
            public readonly Vector4 Limits;

            public FogPayload(
                Vector4 right,
                Vector4 up,
                Vector4 forward,
                Vector4 atmosphere,
                Vector4 sunDir,
                Vector4 horizon,
                Vector4 sun,
                Vector4 limits)
            {
                Right = right;
                Up = up;
                Forward = forward;
                Atmosphere = atmosphere;
                SunDir = sunDir;
                Horizon = horizon;
                Sun = sun;
                Limits = limits;
            }

            public unsafe void WriteBytes(byte[] buffer)
            {
                if (buffer == null || buffer.Length < 128) return;
                fixed (byte* p = buffer)
                {
                    Vector4* v = (Vector4*)p;
                    v[0] = Right;
                    v[1] = Up;
                    v[2] = Forward;
                    v[3] = Atmosphere;
                    v[4] = SunDir;
                    v[5] = Horizon;
                    v[6] = Sun;
                    v[7] = Limits;
                }
            }
        }

        internal static FogPayload pendingPayload;
        private static bool registered;
        private static bool warnedRegisterFail;
        private static DepthFogFeature feature;

        public static bool IsActive => Volatile.Read(ref pendingPayload) != null;

        public static void Update(Camera cam, SkyScenery.Env env, float density, float groundY)
        {
            if (density <= 1e-4f || cam == null)
            {
                Disable();
                return;
            }

            Matrix4x4 p = cam.projectionMatrix;
            Vector3 r = cam.transform.right;
            Vector3 u = cam.transform.up;
            Vector3 f = cam.transform.forward;

            float invP00 = Mathf.Abs(p.m00) > 1e-6f ? (1f / p.m00) : 0f;
            float invP11 = Mathf.Abs(p.m11) > 1e-6f ? (1f / p.m11) : 0f;

            float heightScale = ShaderpackDefaults.FogHeightFalloff * (1f + 2f * env.fog);
            float camHeight = cam.transform.position.y - groundY;

            float nightBrightness = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(env.glow + 0.25f));
            Color sunCol = ToneMap.SunColorFor(env.sunDir);
            float scatterK = Mathf.Clamp01(env.glow) * (0.06f + 0.10f * Mathf.Clamp01(env.twilight)) * (env.sunDir.y > -0.05f ? 1f : 0f);

            Vector4 rightV = new Vector4(r.x, r.y, r.z, invP00);
            Vector4 upV = new Vector4(u.x, u.y, u.z, invP11);
            Vector4 forwardV = new Vector4(f.x, f.y, f.z, p.m22);
            Vector4 atmosphereV = new Vector4(density, heightScale, camHeight, p.m23);
            Vector4 sunDirV = new Vector4(env.sunDir.x, env.sunDir.y, env.sunDir.z, ShaderpackDefaults.MieG);
            Vector4 horizonV = new Vector4(env.horizon.r, env.horizon.g, env.horizon.b, nightBrightness);
            Vector4 sunV = new Vector4(sunCol.r, sunCol.g, sunCol.b, scatterK);
            Vector4 limitsV = new Vector4(cam.farClipPlane, p.m02, p.m12, 0f);

            FogPayload payload = new FogPayload(rightV, upV, forwardV, atmosphereV, sunDirV, horizonV, sunV, limitsV);
            Volatile.Write(ref pendingPayload, payload);

            EnsureRegistered();
        }

        public static void Disable()
        {
            Volatile.Write(ref pendingPayload, null);
        }

        public static void EnsureRegistered()
        {
            if (registered) return;
            try
            {
                EngineApplication app = EngineApplication.Instance;
                if (app == null || app.FrameRenderer == null || app.GraphicsDevice == null || app.RenderContext == null)
                    return;

                var features = app.FrameRenderer.Features;
                PostFxFeature pfx = null;
                int pfxIndex = -1;
                for (int i = 0; i < features.Count; i++)
                {
                    if (features[i] is PostFxFeature found)
                    {
                        pfx = found;
                        pfxIndex = i;
                        break;
                    }
                }

                if (pfxIndex < 0 || pfx == null)
                {
                    if (!warnedRegisterFail)
                    {
                        Log.Warning("[DepthFog3D] PostFxFeature not found in FrameRenderer. Delaying registration.");
                        warnedRegisterFail = true;
                    }
                    return;
                }

                SceneColorTargets sceneTargets = AccessTools.Field(typeof(PostFxFeature), "targets")?.GetValue(pfx) as SceneColorTargets;
                CompositePass composite = AccessTools.Field(typeof(PostFxFeature), "composite")?.GetValue(pfx) as CompositePass;

                if (sceneTargets == null || composite == null)
                {
                    var bootstrapType = AccessTools.TypeByName("RimWorldWaylith.Bootstrap");
                    if (bootstrapType != null)
                    {
                        sceneTargets ??= AccessTools.Field(bootstrapType, "_sceneTargets")?.GetValue(null) as SceneColorTargets;
                        composite ??= AccessTools.Field(bootstrapType, "_composite")?.GetValue(null) as CompositePass;
                    }
                }

                if (sceneTargets == null || composite == null)
                {
                    if (!warnedRegisterFail)
                    {
                        Log.Warning("[DepthFog3D] Unable to resolve SceneColorTargets or CompositePass. Aborting feature insertion.");
                        warnedRegisterFail = true;
                    }
                    return;
                }

                feature = new DepthFogFeature(app.GraphicsDevice, sceneTargets, composite);
                app.FrameRenderer.InsertFeature(pfxIndex, feature);
                registered = true;
            }
            catch (Exception ex)
            {
                if (!warnedRegisterFail)
                {
                    Log.Warning($"[DepthFog3D] EnsureRegistered failed: {ex}");
                    warnedRegisterFail = true;
                }
            }
        }
    }

    public sealed class DepthFogFeature : IRenderFeature, IDisposable
    {
        private readonly DepthFogPass pass;
        private readonly IRenderPass[] passes;

        public bool IsActive => true;
        public IReadOnlyList<IRenderPass> Passes => passes;

        public DepthFogFeature(GraphicsDevice graphicsDevice, SceneColorTargets sceneTargets, CompositePass composite)
        {
            pass = new DepthFogPass(graphicsDevice, sceneTargets, composite);
            passes = new IRenderPass[] { pass };
        }

        public object CaptureFrame(in FrameTiming timing)
        {
            if (!DepthFog3D.IsActive) return null;
            if (LongEventHandler.AnyEventWhichDoesntUseStandardWindowNowOrWaiting || !WorldRendererUtility.DrawingMap || MapGenerator.mapBeingGenerated != null)
                return null;

            Camera cam = CameraRig3D.Cam ?? Current.CameraDriver?.MyCamera;
            if (cam == null || cam.targetTexture != null)
                return null;

            return Volatile.Read(ref DepthFog3D.pendingPayload);
        }

        public void OnRenderTargetRebuilt(IRenderTarget renderTarget)
        {
            pass.OnRenderTargetRebuilt(renderTarget);
        }

        public void Dispose()
        {
            pass.Dispose();
        }
    }

    public sealed class DepthFogPass : IRenderPass, IDisposable
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly SceneColorTargets sceneTargets;
        private readonly CompositePass composite;
        private readonly byte passId;
        private readonly byte[] pushConstantData = new byte[128];

        private RenderTexture[] fogTargets = Array.Empty<RenderTexture>();
        private bool[] ready = Array.Empty<bool>();
        private bool sourceDecorated;
        private Func<int, RenderTexture> originalSource;
        private Func<int, RenderTexture> decoratedSource;

        private DeviceEx device;
        private VkShaderModule vertexModule;
        private VkShaderModule fragmentModule;
        private VkDescriptorSetLayout descriptorSetLayout;
        private VkPipelineLayout pipelineLayout;
        private VkRenderPass renderPass;
        private VkPipeline pipeline;
        private VkDescriptorPool[] descriptorPools = Array.Empty<VkDescriptorPool>();
        private VkFormat currentPipelineFormat = VkFormat.Undefined;
        private bool initialized;
        private bool initFailed;
        private bool disposed;

        public byte PassId => passId;
        public PassAttachment Attachment => null;
        public bool PreBackbuffer => true;

        public DepthFogPass(GraphicsDevice graphicsDevice, SceneColorTargets sceneTargets, CompositePass composite)
        {
            this.graphicsDevice = graphicsDevice;
            this.device = graphicsDevice.Device;
            this.sceneTargets = sceneTargets;
            this.composite = composite;
            this.passId = RenderPassRegistry.Register("DepthFog");
        }

        private void DecorateCompositeSource()
        {
            if (sourceDecorated || composite == null) return;
            originalSource = composite.Source;
            decoratedSource = frameIndex =>
            {
                if (frameIndex >= 0 && frameIndex < ready.Length && ready[frameIndex] && fogTargets != null && frameIndex < fogTargets.Length)
                {
                    return fogTargets[frameIndex];
                }
                return originalSource != null ? originalSource(frameIndex) : sceneTargets?.Current(frameIndex);
            };
            composite.Source = decoratedSource;
            sourceDecorated = true;
        }

        private void RestoreCompositeSource()
        {
            if (composite != null && decoratedSource != null && composite.Source == decoratedSource)
            {
                composite.Source = originalSource;
            }
        }

        public void Record(in FrameContext ctx, object featurePayload)
        {
            int frameIndex = ctx.SubmissionFrameIndex;
            if (frameIndex >= 0 && frameIndex < ready.Length)
            {
                ready[frameIndex] = false;
            }

            DecorateCompositeSource();

            if (featurePayload is not DepthFog3D.FogPayload payload) return;
            if (sceneTargets == null) return;

            RenderTexture sceneTarget = sceneTargets.Current(frameIndex);
            if (sceneTarget == null || sceneTarget.Depth == null) return;

            EnsureTargets(ctx.RenderContext.FramesInFlight, sceneTarget.Width, sceneTarget.Height, sceneTarget.ColorFormat);
            if (fogTargets == null || frameIndex >= fogTargets.Length) return;

            RenderTexture output = fogTargets[frameIndex];
            if (output == null) return;

            VkImageView depthSampleView = sceneTarget.DepthSampleView;
            if (depthSampleView.IsNull()) return;

            EnsureInitialized(in ctx, output.ColorFormat);
            if (!initialized) return;

            CommandBuffer cmd = ctx.CommandBuffer;

            // Pipeline barrier: DepthStencilAttachmentOptimal -> DepthStencilReadOnlyOptimal
            VkImageMemoryBarrier depthBarrier = new VkImageMemoryBarrier
            {
                SrcAccessMask = VkAccessFlags.DepthStencilAttachmentWriteBit,
                DstAccessMask = VkAccessFlags.ShaderReadBit,
                OldLayout = VkImageLayout.DepthStencilAttachmentOptimal,
                NewLayout = VkImageLayout.DepthStencilReadOnlyOptimal,
                SrcQueueFamilyIndex = -1,
                DstQueueFamilyIndex = -1,
                Image = sceneTarget.Depth.Image,
                SubresourceRange = new VkImageSubresourceRange
                {
                    AspectMask = sceneTarget.DepthFormat == VkFormat.D24UNormS8UInt || sceneTarget.DepthFormat == VkFormat.D32SFloatS8UInt
                        ? VkImageAspectFlags.DepthBit | VkImageAspectFlags.StencilBit : VkImageAspectFlags.DepthBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };
            cmd.PipelineBarrier(
                VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit,
                VkPipelineStageFlags.FragmentShaderBit,
                VkDependencyFlags.None,
                default(ReadOnlySpan<VkMemoryBarrier>),
                default(ReadOnlySpan<VkBufferMemoryBarrier>),
                new ReadOnlySpan<VkImageMemoryBarrier>(in depthBarrier)
            );

            // Allocate and write descriptor set
            device.ResetDescriptorPool(descriptorPools[frameIndex], 0);
            VkDescriptorSet descSet = DescriptorSetWriter.AllocateDescriptorSet(device, descriptorPools[frameIndex], descriptorSetLayout);

            unsafe
            {
                VkDescriptorImageInfo* imgInfos = stackalloc VkDescriptorImageInfo[2];
                VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[2];

                imgInfos[0] = new VkDescriptorImageInfo
                {
                    Sampler = sceneTarget.Sampler,
                    ImageView = sceneTarget.Color.ImageView,
                    ImageLayout = VkImageLayout.ShaderReadOnlyOptimal
                };
                writes[0] = new VkWriteDescriptorSet
                {
                    DstSet = descSet,
                    DstBinding = 0,
                    DescriptorCount = 1,
                    DescriptorType = VkDescriptorType.CombinedImageSampler,
                    ImageInfo = &imgInfos[0]
                };

                imgInfos[1] = new VkDescriptorImageInfo
                {
                    Sampler = ComputeDispatchQueue.PointClampSampler,
                    ImageView = depthSampleView,
                    ImageLayout = VkImageLayout.DepthStencilReadOnlyOptimal
                };
                writes[1] = new VkWriteDescriptorSet
                {
                    DstSet = descSet,
                    DstBinding = 1,
                    DescriptorCount = 1,
                    DescriptorType = VkDescriptorType.CombinedImageSampler,
                    ImageInfo = &imgInfos[1]
                };

                device.UpdateDescriptorSets(new ReadOnlySpan<VkWriteDescriptorSet>(writes, 2), default(ReadOnlySpan<VkCopyDescriptorSet>));
            }

            VkExtent2D extent = output.Extent;
            VkRenderPassBeginInfo rpBegin = new VkRenderPassBeginInfo
            {
                RenderPass = renderPass,
                Framebuffer = output.GetFramebuffer(renderPass),
                RenderArea = new VkRect2D(extent),
                ClearValueCount = 0,
                ClearValues = null
            };

            cmd.BeginRenderPass(in rpBegin, VkSubpassContents.Inline);
            cmd.BindPipeline(VkPipelineBindPoint.Graphics, pipeline);

            VkViewport viewport = new VkViewport
            {
                X = 0f,
                Y = 0f,
                Width = extent.Width,
                Height = extent.Height,
                MinDepth = 0f,
                MaxDepth = 1f
            };
            cmd.SetViewport(0, new ReadOnlySpan<VkViewport>(in viewport));

            VkRect2D scissor = new VkRect2D(extent);
            cmd.SetScissor(0, new ReadOnlySpan<VkRect2D>(in scissor));

            cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, pipelineLayout, 0, descSet, default(Span<ByteSize32>));

            payload.WriteBytes(pushConstantData);
            cmd.PushConstants(pipelineLayout, VkShaderStageFlags.FragmentBit, ByteSize.Zero, new ReadOnlySpan<byte>(pushConstantData, 0, 128));

            cmd.Draw(3, 1, 0, 0);
            cmd.EndRenderPass();
            output.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);

            if (Platform.IsMacOS)
            {
                VkImageMemoryBarrier macBarrier = new VkImageMemoryBarrier
                {
                    SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                    DstAccessMask = VkAccessFlags.ShaderReadBit,
                    OldLayout = VkImageLayout.ShaderReadOnlyOptimal,
                    NewLayout = VkImageLayout.ShaderReadOnlyOptimal,
                    SrcQueueFamilyIndex = -1,
                    DstQueueFamilyIndex = -1,
                    Image = output.Color.Image,
                    SubresourceRange = new VkImageSubresourceRange
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };
                cmd.PipelineBarrier(
                    VkPipelineStageFlags.ColorAttachmentOutputBit,
                    VkPipelineStageFlags.FragmentShaderBit,
                    VkDependencyFlags.None,
                    default(ReadOnlySpan<VkMemoryBarrier>),
                    default(ReadOnlySpan<VkBufferMemoryBarrier>),
                    new ReadOnlySpan<VkImageMemoryBarrier>(in macBarrier)
                );
            }

            if (frameIndex >= 0 && frameIndex < ready.Length)
            {
                ready[frameIndex] = true;
            }
        }

        private void EnsureTargets(int framesInFlight, int w, int h, VkFormat colorFormat)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);

            if (fogTargets.Length == framesInFlight && fogTargets.Length > 0
                && fogTargets[0].Width == w && fogTargets[0].Height == h
                && fogTargets[0].ColorFormat == colorFormat)
            {
                return;
            }

            if (fogTargets.Length != 0)
            {
                device.WaitIdle();
                Array.Clear(ready);
                if (fogTargets[0].ColorFormat != colorFormat || descriptorPools.Length != framesInFlight)
                    Invalidate();
            }

            if (fogTargets.Length != framesInFlight || (fogTargets.Length > 0 && fogTargets[0].ColorFormat != colorFormat))
            {
                DisposeTargets();
                fogTargets = new RenderTexture[framesInFlight];
                ready = new bool[framesInFlight];
                for (int i = 0; i < framesInFlight; i++)
                {
                    fogTargets[i] = new RenderTexture(w, h, colorFormat, VkFormat.Undefined, RenderTextureFilter.Bilinear, RenderTextureWrap.Clamp, false, $"DepthFogTarget_{i}");
                }
            }
            else
            {
                for (int j = 0; j < framesInFlight; j++)
                {
                    fogTargets[j].Resize(w, h);
                }
            }
        }

        private void EnsureInitialized(in FrameContext ctx, VkFormat colorFormat)
        {
            if (initialized && currentPipelineFormat == colorFormat) return;
            if (initialized && currentPipelineFormat != colorFormat)
            {
                device.WaitIdle();
                Invalidate();
            }
            if (initFailed) return;

            device = ctx.GraphicsDevice.Device;

            try
            {
                string shaderPath = ResolveFragmentShaderPath();
                string shaderSource = File.Exists(shaderPath) ? File.ReadAllText(shaderPath) : null;
                if (string.IsNullOrEmpty(shaderSource))
                {
                    initFailed = true;
                    Log.Error($"[DepthFog3D] Shader source not found at: {shaderPath}");
                    return;
                }

                currentPipelineFormat = colorFormat;
                renderPass = PostFxPassHelpers.CreateColorRenderPass(device, colorFormat);
                vertexModule = PostFxPassHelpers.CompileFullscreenVertex(device);
                fragmentModule = PostFxPassHelpers.Compile(device, shaderSource, RuntimeShaderStage.Fragment, "DepthFog.frag.glsl");

                descriptorSetLayout = CreateDescriptorSetLayout();
                pipelineLayout = CreatePipelineLayout();
                pipeline = PostFxPassHelpers.CreateFullscreenPipeline(device, renderPass, VkSampleCountFlags._1Bit, vertexModule, fragmentModule, pipelineLayout, BlendState.Opaque, "DepthFog");
                descriptorPools = CreateDescriptorPools(ctx.RenderContext.FramesInFlight);

                initialized = true;
            }
            catch (Exception ex)
            {
                initFailed = true;
                Log.Error($"[DepthFog3D] Failed to initialize pipeline: {ex}");
                CleanupPartialInit();
            }
        }

        private static string ResolveFragmentShaderPath()
        {
            try
            {
                string asmLocation = typeof(DepthFog3D).Assembly.Location;
                if (!string.IsNullOrEmpty(asmLocation))
                {
                    string asmDir = Path.GetDirectoryName(asmLocation);
                    string parent = Path.GetDirectoryName(asmDir);
                    string candidate = Path.Combine(parent, "Shaders", "DepthFog.frag.glsl");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            string fallback = Path.Combine(AppContext.BaseDirectory, "Mods", "FirstPerson3D", "1.7", "Shaders", "DepthFog.frag.glsl");
            if (File.Exists(fallback)) return fallback;

            return "DepthFog.frag.glsl";
        }

        private unsafe VkDescriptorSetLayout CreateDescriptorSetLayout()
        {
            VkDescriptorSetLayoutBinding* b = stackalloc VkDescriptorSetLayoutBinding[2];
            b[0] = new VkDescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = VkShaderStageFlags.FragmentBit
            };
            b[1] = new VkDescriptorSetLayoutBinding
            {
                Binding = 1,
                DescriptorType = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = VkShaderStageFlags.FragmentBit
            };

            return device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo
            {
                Bindings = new Span<VkDescriptorSetLayoutBinding>(b, 2)
            }, device.VkAllocator);
        }

        private VkPipelineLayout CreatePipelineLayout()
        {
            VkDescriptorSetLayout setRef = descriptorSetLayout;
            VkPushConstantRange pushRange = new VkPushConstantRange
            {
                StageFlags = VkShaderStageFlags.FragmentBit,
                Offset = ByteSize.Zero,
                Size = ByteSize.Of(128)
            };

            return device.CreatePipelineLayout(
                new ReadOnlySpan<VkDescriptorSetLayout>(in setRef),
                new ReadOnlySpan<VkPushConstantRange>(in pushRange),
                device.VkAllocator
            );
        }

        private unsafe VkDescriptorPool[] CreateDescriptorPools(int framesInFlight)
        {
            VkDescriptorPool[] array = new VkDescriptorPool[framesInFlight];
            for (int i = 0; i < framesInFlight; i++)
            {
                VkDescriptorPoolSize poolSize = new VkDescriptorPoolSize
                {
                    Type = VkDescriptorType.CombinedImageSampler,
                    DescriptorCount = 2
                };
                array[i] = device.CreateDescriptorPool(new VkDescriptorPoolCreateInfo
                {
                    MaxSets = 1,
                    PoolSizeCount = 1,
                    PoolSizes = &poolSize
                }, device.VkAllocator);
            }
            return array;
        }

        public void OnRenderTargetRebuilt(IRenderTarget target)
        {
            if (sceneTargets != null && target is WindowRenderContext renderContext)
            {
                try
                {
                    device.WaitIdle();
                    RenderTexture scene0 = sceneTargets.Current(0);
                    if (scene0 != null)
                    {
                        EnsureTargets(renderContext.FramesInFlight, scene0.Width, scene0.Height, scene0.ColorFormat);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[DepthFog3D] OnRenderTargetRebuilt target resize error: {ex}");
                }
            }
            Invalidate();
        }

        private void Invalidate()
        {
            if (!initialized) return;
            CleanupPartialInit();
            initialized = false;
        }

        private void CleanupPartialInit()
        {
            if (descriptorPools != null)
            {
                foreach (VkDescriptorPool pool in descriptorPools)
                {
                    if (!pool.IsNull()) device.DestroyDescriptorPool(pool, device.VkAllocator);
                }
                descriptorPools = Array.Empty<VkDescriptorPool>();
            }

            if (!pipeline.IsNull())
            {
                device.DestroyPipeline(pipeline, device.VkAllocator);
                pipeline = default(VkPipeline);
            }

            if (!renderPass.IsNull())
            {
                device.DestroyRenderPass(renderPass, device.VkAllocator);
                renderPass = default(VkRenderPass);
            }

            if (!pipelineLayout.IsNull())
            {
                device.DestroyPipelineLayout(pipelineLayout, device.VkAllocator);
                pipelineLayout = default(VkPipelineLayout);
            }

            if (!descriptorSetLayout.IsNull())
            {
                device.DestroyDescriptorSetLayout(descriptorSetLayout, device.VkAllocator);
                descriptorSetLayout = default(VkDescriptorSetLayout);
            }

            if (!fragmentModule.IsNull())
            {
                device.DestroyShaderModule(fragmentModule, device.VkAllocator);
                fragmentModule = default(VkShaderModule);
            }

            if (!vertexModule.IsNull())
            {
                device.DestroyShaderModule(vertexModule, device.VkAllocator);
                vertexModule = default(VkShaderModule);
            }
        }

        private void DisposeTargets()
        {
            for (int i = 0; i < fogTargets.Length; i++)
            {
                fogTargets[i]?.Dispose();
            }
            fogTargets = Array.Empty<RenderTexture>();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RestoreCompositeSource();
            Invalidate();
            DisposeTargets();
        }
    }
}
