using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LookingGlass {
    internal static class MultiViewRendering {
        [Serializable]
        private struct ViewInterpolationProperties {
            private bool initialized;
            public bool Initialized => initialized;

            public ShaderPropertyId result;
            public ShaderPropertyId resultDepth;
            public ShaderPropertyId nearClip;
            public ShaderPropertyId farClip;
            public ShaderPropertyId focalDist;
            public ShaderPropertyId perspw;
            public ShaderPropertyId viewSize;
            public ShaderPropertyId viewPositions;
            public ShaderPropertyId viewOffsets;
            public ShaderPropertyId baseViewPositions;
            public ShaderPropertyId spanSize;
            public ShaderPropertyId px;

            public void InitializeAll() {
                initialized = true;

                result = "Result";
                resultDepth = "ResultDepth";
                nearClip = "_NearClip";
                farClip = "_FarClip";
                focalDist = "focalDist";
                perspw = "perspw";
                viewSize = "viewSize";
                viewPositions = "viewPositions";
                viewOffsets = "viewOffsets";
                baseViewPositions = "baseViewPositions";
                spanSize = "spanSize";
                px = "px";

            }
        }
        [Serializable]
        private struct LightfieldProperties {
            private bool initialized;
            public bool Initialized => initialized;

            public ShaderPropertyId pitch;
            public ShaderPropertyId slope;
            public ShaderPropertyId center;
            public ShaderPropertyId subpixelSize;
            public ShaderPropertyId tile;
            public ShaderPropertyId viewPortion;
            public ShaderPropertyId aspect; //NOTE: CORRESPONDS TO Calibration.aspect
            public ShaderPropertyId verticalOffset;

            public void InitializeAll() {
                initialized = true;

                pitch = "pitch";
                slope = "slope";
                center = "center";
                subpixelSize = "subpixelSize";
                tile = "tile";
                viewPortion = "viewPortion";
                aspect = "aspect";
                verticalOffset = "verticalOffset";
            }
        }

        [Serializable]
        private struct RenderViewSharedData {
            public Camera singleViewCamera;
            public HologramRenderSettings renderSettings;
            public float aspect;
            public Matrix4x4 centerViewMatrix;
            public Matrix4x4 centerProjMatrix;
            public float viewConeSweep;
            public float projModifier;
            public bool isNonDefaultDepthiness;
            public Matrix4x4 depthinessMatrix;
            public RenderTexture quilt;
            public RenderTexture quiltRTDepth;
            public Action<int> onViewRender;
        }

        private static ComputeShader interpolationComputeShader;
        private static ViewInterpolationProperties interpolationProperties;
        private static LightfieldProperties lightfieldProperties;
        private static Material copyRGB_AMaterial;

        internal static void ClearBeforeRendering(HologramCamera hologramCamera) {
            HologramCameraProperties cameraData = hologramCamera.CameraProperties;
            Clear(hologramCamera.QuiltTexture, cameraData.ClearFlags, cameraData.BackgroundColor);
        }

        internal static void Clear(RenderTexture renderTarget, CameraClearFlags clearFlags, Color color) {
            bool clearDepth = clearFlags == CameraClearFlags.Depth;
            bool clearColor = clearFlags == CameraClearFlags.SolidColor;

            if (clearDepth || clearColor) {
                RenderTexture prev = RenderTexture.active;
                try {
                    RenderTexture.active = renderTarget;
                    GL.Clear(
                        clearDepth || clearColor,
                        clearColor,
                        color,
                        1
                    );
                } finally {
                    RenderTexture.active = prev;
                }
            }
        }

        internal static void RenderQuilt(HologramCamera hologramCamera, bool ignorePostProcessing, Action<int> onViewRender) {
            hologramCamera.UpdateLightfieldMaterial();

            HologramCameraProperties cameraData = hologramCamera.CameraProperties;
            Camera singleViewCamera = hologramCamera.SingleViewCamera;
            Calibration cal = hologramCamera.UnmodifiedCalibration;
            float focalPlane = cameraData.FocalPlane;
            float depthiness = cameraData.Depthiness;
            float size = cameraData.Size;
            float viewCone = cal.viewCone == 0 ? Calibration.DEFAULT_VIEWCONE : cal.viewCone;
            Assert.AreNotEqual(0, viewCone, "The viewCone should be non-zero! When zero, all the single-views may render the same image.");

            RenderViewSharedData data = new RenderViewSharedData() {
                singleViewCamera = singleViewCamera,
                renderSettings = hologramCamera.RenderSettings,
                centerViewMatrix = singleViewCamera.worldToCameraMatrix,
                centerProjMatrix = singleViewCamera.projectionMatrix,
                isNonDefaultDepthiness = depthiness != 1,
                quilt = hologramCamera.QuiltTexture,
                onViewRender = onViewRender
            };

            data.aspect = hologramCamera.Aspect;
            data.viewConeSweep = (-focalPlane * Mathf.Tan(viewCone * 0.5f * Mathf.Deg2Rad) * 2);
            data.projModifier = 1 / (size * data.aspect); //The projection matrices must be modified in terms of focal plane size
            Assert.AreNotEqual(0, data.viewConeSweep, "The viewConeSweep should be non-zero! When zero, all the single-views may render the same image.");

            RenderTexture depthQuiltTex = hologramCamera.DepthQuiltTexture;
            if (depthQuiltTex == null) {
                depthQuiltTex = CreateQuiltDepthTexture(data.quilt, false);
                hologramCamera.DepthQuiltTexture = data.quiltRTDepth = depthQuiltTex;
            } else {
                data.quiltRTDepth = depthQuiltTex;
            }

            if (data.isNonDefaultDepthiness) {
                Matrix4x4 transposeMatrix = Matrix4x4.Translate(new Vector3(0, 0, focalPlane));
                Matrix4x4 scaleMatrix = Matrix4x4.Scale(new Vector3(1, 1, cameraData.Depthiness));
                Matrix4x4 untransposeMatrix = Matrix4x4.Translate(new Vector3(0, 0, -focalPlane));

                data.depthinessMatrix = untransposeMatrix * scaleMatrix * transposeMatrix;
            }

#if UNITY_POST_PROCESSING_STACK_V2
            bool hasPPCamera = false;
            if (!ignorePostProcessing) {
                Camera postProcessCamera = hologramCamera.PostProcessCamera;
                hasPPCamera = postProcessCamera != null;
                if (hasPPCamera)
                    postProcessCamera.CopyFrom(data.singleViewCamera);
            }
#endif
            //NOTE: This FOV trick is on purpose, to keep shadows from disappearing.

            //We use a large 135° FOV so that lights and shadows DON'T get culled out in our individual single-views!
            //But, this FOV is ignored when we actually render, because we modify the camera matrices.
            //So, we get the best of both worlds -- rendering correctly with no issues with culling.
            if (RenderPipelineUtil.IsBuiltIn)
                data.singleViewCamera.fieldOfView = 135;
            else
                data.singleViewCamera.fieldOfView += 35;
            data.singleViewCamera.aspect = data.aspect;

            int viewInterpolation = hologramCamera.Optimization.GetViewInterpolation(data.renderSettings.numViews);
            int onlyShowViewIndex = hologramCamera.Debugging.OnlyShowView;
            if (onlyShowViewIndex > -1) {
                bool copyViewToAllTiles = !hologramCamera.Debugging.OnlyRenderOneView;
                RenderView(onlyShowViewIndex, ref data, copyViewToAllTiles,
                    out RenderTexture viewRT,
                    out RenderTexture viewRTRFloat
                );

                if (copyViewToAllTiles) {
                    for (int i = 0; i < data.renderSettings.numViews; i++) {
                        if (i == onlyShowViewIndex)
                            continue;
                        //Instead of re-rendering the single-view so many times, we can just copy it across the quilt way faster!
                        CopyViewToQuilt(data.renderSettings, i, viewRT, data.quilt);
                        CopyViewToQuilt(data.renderSettings, i, viewRTRFloat, data.quiltRTDepth);
                    }

                    RenderTexture.ReleaseTemporary(viewRT);
                    RenderTexture.ReleaseTemporary(viewRTRFloat);
                }
            } else {
                for (int i = 0; i < data.renderSettings.numViews; i++) {
                    if (i % viewInterpolation != 0 && i != data.renderSettings.numViews - 1)
                        continue;
                    RenderView(i, ref data);
                }
                // onViewRender final pass
                onViewRender?.Invoke(data.renderSettings.numViews);
            }

            //Reset stuff back to what they were originally:
            //NOTE: We DON'T call these reset matrix methods, because our "default" matrices are customized
            //in LookingGlass.ResetCameras() to include things like the focalPlane and frustum shifting.
            //data.singleViewCamera.ResetWorldToCameraMatrix();
            //data.singleViewCamera.ResetProjectionMatrix();

            data.singleViewCamera.worldToCameraMatrix = data.centerViewMatrix;
            data.singleViewCamera.projectionMatrix = data.centerProjMatrix;
            data.singleViewCamera.fieldOfView = cameraData.FieldOfView;

            // if interpolation is happening, release
            if (viewInterpolation > 1) {
                //TODO: interpolate on the quilt itself
                InterpolateViewsOnQuilt(hologramCamera, data.quilt, data.quiltRTDepth);
            }

#if UNITY_POST_PROCESSING_STACK_V2
            if (hasPPCamera && !ignorePostProcessing) {
#if !UNITY_2018_1_OR_NEWER
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 ||
                    SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12) {
                    FlipRenderTexture(data.quilt);
                }
#endif
                RunPostProcess(hologramCamera, data.quilt, data.quiltRTDepth);
            }
#endif
            SimpleDOF dof = hologramCamera.GetComponent<SimpleDOF>();
            if (dof != null && dof.enabled) {
                dof.DoDOF(data.quilt, data.quiltRTDepth);
            }
        }

        private static RenderTexture CreateQuiltDepthTexture(RenderTexture quilt, bool isTemporary) {
            RenderTextureDescriptor depthDescriptor = quilt.descriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.RFloat;
            RenderTexture quiltRTDepth = (isTemporary) ? RenderTexture.GetTemporary(depthDescriptor) : new RenderTexture(depthDescriptor);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = quiltRTDepth;
            GL.Clear(true, true, Color.black, 1);
            RenderTexture.active = prev;

            return quiltRTDepth;
        }

        private static void RenderView(int viewIndex, ref RenderViewSharedData data)
            => RenderView(viewIndex, ref data, false, out _, out _);
        private static void RenderView(int viewIndex, ref RenderViewSharedData data, bool persistViewTextures, out RenderTexture viewRT, out RenderTexture viewRTRFloat) {
            //TODO: Is there a reason we don't notify after the view has **finished** rendering? (below, at the bottom of this for loop block)
            data.onViewRender?.Invoke(viewIndex);

            viewRT = RenderTexture.GetTemporary(data.renderSettings.ViewWidth, data.renderSettings.ViewHeight, 24);
            RenderTexture viewRTDepth = null;
            if (RenderPipelineUtil.IsBuiltIn) {
                viewRTDepth = RenderTexture.GetTemporary(data.renderSettings.ViewWidth, data.renderSettings.ViewHeight, 24, RenderTextureFormat.Depth);
                data.singleViewCamera.SetTargetBuffers(viewRT.colorBuffer, viewRTDepth.depthBuffer);
            } else {
                data.singleViewCamera.targetTexture = viewRT;
            }

            Matrix4x4 viewMatrix = data.centerViewMatrix;
            Matrix4x4 projMatrix = data.centerProjMatrix;

            float currentViewLerp = 0; // if numviews is 1, take center view
            if (data.renderSettings.numViews > 1)
                currentViewLerp = (float) viewIndex / (data.renderSettings.numViews - 1) - 0.5f;

            //NOTE:
            //m03 is x shift        (m03 is 1st row, 4th column)
            //m13 is y shift        (m13 is 2st row, 4th column)
            //m23 is z shift        (m23 is 3rd row, 4th column)
            viewMatrix.m03 += currentViewLerp * data.viewConeSweep;

            projMatrix.m02 += currentViewLerp * data.viewConeSweep * data.projModifier;
            data.singleViewCamera.worldToCameraMatrix = viewMatrix;
            data.singleViewCamera.projectionMatrix = (data.isNonDefaultDepthiness) ? projMatrix * data.depthinessMatrix : projMatrix;

            data.singleViewCamera.Render();

            CopyViewToQuilt(data.renderSettings, viewIndex, viewRT, data.quilt);
            data.singleViewCamera.targetTexture = null;

            switch (RenderPipelineUtil.GetRenderPipelineType()) {
                case RenderPipelineType.BuiltIn:
                    // gotta create a weird new viewRT now
                    RenderTextureDescriptor viewRTRFloatDesc = viewRT.descriptor;
                    viewRTRFloatDesc.colorFormat = RenderTextureFormat.RFloat;
                    viewRTRFloat = RenderTexture.GetTemporary(viewRTRFloatDesc);
                    Graphics.Blit(viewRTDepth, viewRTRFloat);
                    RenderTexture.ReleaseTemporary(viewRTDepth);

                    CopyViewToQuilt(data.renderSettings, viewIndex, viewRTRFloat, data.quiltRTDepth);

                    if (!persistViewTextures) {
                        RenderTexture.ReleaseTemporary(viewRT);
                        RenderTexture.ReleaseTemporary(viewRTRFloat);
                        viewRT = null;
                        viewRTRFloat = null;
                    }

                    //NOTE: This helps 3D cursor ReadPixels faster
                    GL.Flush();
                    break;
                default:
                    RenderTexture.ReleaseTemporary(viewRT);
                    viewRT = null;
                    viewRTRFloat = null;
                    break;
            }
        }

        private static Rect GetViewRect(HologramRenderSettings renderSettings, int viewIndex) {
            int reversedViewIndex = renderSettings.viewColumns * renderSettings.viewRows - viewIndex - 1;

            int targetX = (viewIndex % renderSettings.viewColumns) * renderSettings.ViewWidth;
            int targetY = (reversedViewIndex / renderSettings.viewColumns) * renderSettings.ViewHeight + renderSettings.PaddingVertical; //NOTE: Reversed here because Y is taken from the top

            return new Rect(targetX, targetY, renderSettings.ViewWidth, renderSettings.ViewHeight);
        }

        /// <summary>
        /// <para>Copies <paramref name="view"/> to every tile in the given <paramref name="quilt"/> texture.</para>
        /// <para>This is useful for copying a 2D view to a quilt, so they can render together with the lightfield shader.</para>
        /// </summary>
        /// <param name="renderSettings">The quilt settings that correspond to <paramref name="quilt"/>.</param>
        /// <param name="view">The view that will be copied to every tile of the <paramref name="quilt"/>.</param>
        /// <param name="quilt">The target texture that will have the <paramref name="view"/> copied over all of its tiles.</param>
        public static void CopyViewToAllQuiltTiles(HologramRenderSettings renderSettings, Texture view, RenderTexture quilt) {
            for (int v = 0; v < renderSettings.numViews; v++)
                MultiViewRendering.CopyViewToQuilt(renderSettings, v, view, quilt);
        }

        /// <summary>
        /// Copies an entire texture into the single-view tile of a quilt.
        /// </summary>
        public static void CopyViewToQuilt(HologramRenderSettings renderSettings, int viewIndex, Texture view, RenderTexture quilt) {
            //NOTE: not using Graphics.CopyTexture(...) because it's an exact per-pixel copy (100% overwrite, no alpha-blending support).
            Rect viewRect = GetViewRect(renderSettings, viewIndex);

            Graphics.SetRenderTarget(quilt);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, renderSettings.quiltWidth, renderSettings.quiltHeight, 0);
            Graphics.DrawTexture(viewRect, view);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
        }

        /// <summary>
        /// Copies a single-view from one quilt to another quilt.
        /// </summary>
        public static void CopyViewBetweenQuilts(HologramRenderSettings fromRenderSettings, int fromView, RenderTexture fromQuilt,
            HologramRenderSettings toRenderSettings, int toView, RenderTexture toQuilt) {

            Rect fromRect = GetViewRect(fromRenderSettings, fromView);
            Rect toRect = GetViewRect(toRenderSettings, toView);

            //NOTE: I'm not sure why we have to manually flip our coordinates, I'm hoping this is expected by Unity when (SystemInfo.graphicsUVStartsAtTop == true)
            //Without this, our quilts were copying in reverse!
            if (SystemInfo.graphicsUVStartsAtTop)
                fromRect.y = (fromQuilt.height - fromRenderSettings.ViewHeight) - fromRect.y;

            Rect normalizedFromRect = new Rect(
                fromRect.x / fromQuilt.width,
                fromRect.y / fromQuilt.height,
                fromRect.width / fromQuilt.width,
                fromRect.height / fromQuilt.height
            );

            Graphics.SetRenderTarget(toQuilt);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, toRenderSettings.quiltWidth, toRenderSettings.quiltHeight, 0);
            Graphics.DrawTexture(toRect, fromQuilt, normalizedFromRect, 0, 0, 0, 0);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
        }

        /// <summary>
        /// Copies a single-view from one quilt to the <paramref name="destination"/> texture.
        /// </summary>
        public static void CopyViewFromQuilt(HologramRenderSettings fromRenderSettings, int fromView, RenderTexture fromQuilt, RenderTexture destination) {
            if (fromQuilt == null)
                throw new ArgumentNullException(nameof(fromQuilt));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            Rect fromRect = GetViewRect(fromRenderSettings, fromView);
            Rect toRect = new Rect(0, 0, destination.width, destination.height);

            //NOTE: I'm not sure why we have to manually flip our coordinates, I'm hoping this is expected by Unity when (SystemInfo.graphicsUVStartsAtTop == true)
            //Without this, our quilts were copying in reverse!
            if (SystemInfo.graphicsUVStartsAtTop)
                fromRect.y = (fromQuilt.height - fromRenderSettings.ViewHeight) - fromRect.y;

            Rect normalizedFromRect = new Rect(
                fromRect.x / fromQuilt.width,
                fromRect.y / fromQuilt.height,
                fromRect.width / fromQuilt.width,
                fromRect.height / fromQuilt.height
            );

            Graphics.SetRenderTarget(destination);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, destination.width, destination.height, 0);
            Graphics.DrawTexture(toRect, fromQuilt, normalizedFromRect, 0, 0, 0, 0);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
        }

        /// <summary>
        /// Applies post-processing effects to the <paramref name="target"/> texture.<br />
        /// Note that this method does NOT draw anything to the screen. It only writes into the <paramref name="target"/> render texture.
        /// </summary>
        /// <param name="hologramCamera">The instance associated with the post-processing. Its post-processing camera is used if <paramref name="postProcessCamera"/> is <c>null</c>.</param>
        /// <param name="target">The render texture to apply post-processing into.</param>
        /// <param name="depthTexture">The depth texture to use for post-processing effects. This is useful, because you can provide a custom depth texture instead of always using a single <see cref="HologramCamera"/>'s depth texture.</param>
        /// <param name="postProcessCamera">A custom post-processing camera, if any. When set to <c>null</c>, the <paramref name="hologramCamera"/>'s built-in post-processing camera is used.</param>
        public static void RunPostProcess(HologramCamera hologramCamera, RenderTexture target, Texture depthTexture, Camera postProcessCamera = null) {
            RenderTexture previousAlpha = null;
            RenderTexture rgbaMix = null;

            bool preserveAlpha = true;
            if (preserveAlpha) {
                previousAlpha = RenderTexture.GetTemporary(target.width, target.height);
                rgbaMix = RenderTexture.GetTemporary(target.width, target.height);
                Graphics.Blit(target, previousAlpha);
            }

            if (postProcessCamera == null)
                postProcessCamera = hologramCamera.PostProcessCamera;
            postProcessCamera.cullingMask = 0;
            postProcessCamera.clearFlags = CameraClearFlags.Nothing;
            postProcessCamera.targetTexture = target;

            Shader.SetGlobalTexture("_FAKEDepthTexture", depthTexture);
            postProcessCamera.Render();

            if (preserveAlpha) {
                if (copyRGB_AMaterial == null)
                    copyRGB_AMaterial = new Material(Util.FindShader("LookingGlass/Copy RGB-A"));
                copyRGB_AMaterial.SetTexture("_ColorTex", target);
                copyRGB_AMaterial.SetTexture("_AlphaTex", previousAlpha);
                Graphics.Blit(null, rgbaMix, copyRGB_AMaterial);
                Graphics.Blit(rgbaMix, target);
                RenderTexture.ReleaseTemporary(previousAlpha);
                RenderTexture.ReleaseTemporary(rgbaMix);
            }
        }

        internal static RenderTexture RenderPreview2D(HologramCamera hologramCamera, bool ignorePostProcessing = false) {
            Profiler.BeginSample(nameof(RenderPreview2D), hologramCamera);
            try {
                Profiler.BeginSample("Create " + nameof(RenderTexture) + "s", hologramCamera);
                int width = hologramCamera.ScreenWidth;
                int height = hologramCamera.ScreenHeight;
                RenderTexture preview2DRT = hologramCamera.Preview2DRT;
                Camera singleViewCamera = hologramCamera.SingleViewCamera;
                Camera postProcessCamera = hologramCamera.PostProcessCamera;

                if (preview2DRT == null
                    || preview2DRT.width != width
                    || preview2DRT.height != height) {
                    preview2DRT = new RenderTexture(width, height, 24);
                }
                RenderTexture depth = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.Depth);
                Profiler.EndSample();

                Profiler.BeginSample("Rendering", hologramCamera);
                try {
                    singleViewCamera.SetTargetBuffers(preview2DRT.colorBuffer, depth.depthBuffer);
                    singleViewCamera.Render();

#if UNITY_POST_PROCESSING_STACK_V2
                    bool hasPPCam = postProcessCamera != null;
                    if (hasPPCam && !ignorePostProcessing) {
                        postProcessCamera.CopyFrom(singleViewCamera);
#if !UNITY_2018_1_OR_NEWER
                        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 ||
                            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12)
                            FlipRenderTexture(preview2DRT);
#endif
                        RunPostProcess(hologramCamera, preview2DRT, depth);
                    }
#endif
                } finally {
                    RenderTexture.ReleaseTemporary(depth);
                    Profiler.EndSample();
                }
                return preview2DRT;
            } finally {
                Profiler.EndSample();
            }
        }

        public static void FlipRenderTexture(RenderTexture texture) {
            RenderTexture rtTemp = RenderTexture.GetTemporary(texture.descriptor);
            rtTemp.Create();
            Graphics.CopyTexture(texture, rtTemp);
            Graphics.SetRenderTarget(texture);
            Rect rtRect = new Rect(0, 0, texture.width, texture.height);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rtRect.width, 0, rtRect.height);
            Graphics.DrawTexture(rtRect, rtTemp);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
            RenderTexture.ReleaseTemporary(rtTemp);
        }

        public static void FlipGenericRenderTexture(RenderTexture texture) {
            RenderTexture rtTemp = RenderTexture.GetTemporary(texture.descriptor);
            rtTemp.Create();
            Graphics.CopyTexture(texture, rtTemp);
            Graphics.SetRenderTarget(texture);
            Rect rtRect = new Rect(0, 0, texture.width, texture.height);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, texture.width, 0, texture.height);
            Graphics.DrawTexture(rtRect, rtTemp);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
            RenderTexture.ReleaseTemporary(rtTemp);
        }

        public static void InterpolateViewsOnQuilt(HologramCamera hologramCamera, RenderTexture quilt, RenderTexture quiltRTDepth) {
            if (interpolationComputeShader == null)
                interpolationComputeShader = Resources.Load<ComputeShader>("ViewInterpolation");
            Assert.IsNotNull(interpolationComputeShader);

            if (!interpolationProperties.Initialized)
                interpolationProperties.InitializeAll();

            Calibration cal = (hologramCamera.AreRenderSettingsLockedForRecording) ? hologramCamera.UnmodifiedCalibration : hologramCamera.Calibration;
            Camera singleViewCamera = hologramCamera.SingleViewCamera;
            HologramCameraProperties cameraData = hologramCamera.CameraProperties;
            HologramRenderSettings renderSettings = hologramCamera.RenderSettings;
            OptimizationProperties optimization = hologramCamera.Optimization;
            int viewInterpolation = optimization.GetViewInterpolation(renderSettings.numViews);

            int kernelFwd = interpolationComputeShader.FindKernel("QuiltInterpolationForward");
            int kernelBack = optimization.BlendViews ?
                interpolationComputeShader.FindKernel("QuiltInterpolationBackBlend") :
                interpolationComputeShader.FindKernel("QuiltInterpolationBack");
            int kernelFwdFlicker = interpolationComputeShader.FindKernel("QuiltInterpolationForwardFlicker");
            int kernelBackFlicker = optimization.BlendViews ?
                interpolationComputeShader.FindKernel("QuiltInterpolationBackBlendFlicker") :
                interpolationComputeShader.FindKernel("QuiltInterpolationBackFlicker");

            interpolationComputeShader.SetTexture(kernelFwd, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelFwd, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetTexture(kernelBack, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelBack, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetTexture(kernelFwdFlicker, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelFwdFlicker, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetTexture(kernelBackFlicker, interpolationProperties.result, quilt);
            interpolationComputeShader.SetTexture(kernelBackFlicker, interpolationProperties.resultDepth, quiltRTDepth);
            interpolationComputeShader.SetFloat(interpolationProperties.nearClip, singleViewCamera.nearClipPlane);
            interpolationComputeShader.SetFloat(interpolationProperties.farClip, singleViewCamera.farClipPlane);
            interpolationComputeShader.SetFloat(interpolationProperties.focalDist, hologramCamera.CameraProperties.FocalPlane);

            //Used for perspective w component:
            float aspectCorrectedFOV = Mathf.Atan(cal.GetAspect() * Mathf.Tan(0.5f * cameraData.FieldOfView * Mathf.Deg2Rad));
            interpolationComputeShader.SetFloat(interpolationProperties.perspw, 2 * Mathf.Tan(aspectCorrectedFOV));
            interpolationComputeShader.SetVector(interpolationProperties.viewSize, new Vector4(
                renderSettings.ViewWidth,
                renderSettings.ViewHeight,
                1f / renderSettings.ViewWidth,
                1f / renderSettings.ViewHeight
            ));

            List<int> viewPositions = new List<int>();
            List<float> viewOffsets = new List<float>();
            List<int> baseViewPositions = new List<int>();
            int validViewIndex = -1;
            int currentInterp = 1;
            for (int i = 0; i < renderSettings.numViews; i++) {
                var positions = new[] {
                    i % renderSettings.viewColumns * renderSettings.ViewWidth,
                    i / renderSettings.viewColumns * renderSettings.ViewHeight,
                };
                if (i != 0 && i != renderSettings.numViews - 1 && i % viewInterpolation != 0) {
                    viewPositions.AddRange(positions);
                    viewPositions.AddRange(new[] { validViewIndex, validViewIndex + 1 });
                    int div = Mathf.Min(viewInterpolation, renderSettings.numViews - 1);
                    int divTotal = renderSettings.numViews / div;
                    if (i > divTotal * viewInterpolation) {
                        div = renderSettings.numViews - divTotal * viewInterpolation;
                    }

                    // from urp-integration
                    // float viewCone = cal.viewCone == 0 ? Calibration.DEFAULT_VIEWCONE : cal.viewCone;

                    float viewCone = Application.isPlaying && cal.viewCone == 0 ? Calibration.DEFAULT_VIEWCONE : cal.viewCone;
                    float offset = div * Mathf.Tan(viewCone * Mathf.Deg2Rad) / (renderSettings.numViews - 1f);
                    float lerp = (float) currentInterp / div;
                    currentInterp++;
                    viewOffsets.AddRange(new[] { offset, lerp });
                } else {
                    baseViewPositions.AddRange(positions);
                    validViewIndex++;
                    currentInterp = 1;
                }
            }

            int viewCount = viewPositions.Count / 4;
            ComputeBuffer viewPositionsBuffer = new ComputeBuffer(viewPositions.Count / 4, 4 * sizeof(int));
            ComputeBuffer viewOffsetsBuffer = new ComputeBuffer(viewOffsets.Count / 2, 2 * sizeof(float));
            ComputeBuffer baseViewPositionsBuffer = new ComputeBuffer(baseViewPositions.Count / 2, 2 * sizeof(int));
            viewPositionsBuffer.SetData(viewPositions);
            viewOffsetsBuffer.SetData(viewOffsets);
            baseViewPositionsBuffer.SetData(baseViewPositions);

            interpolationComputeShader.SetBuffer(kernelFwd, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwd, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwd, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBack, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBack, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelBack, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwdFlicker, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwdFlicker, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelFwdFlicker, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBackFlicker, interpolationProperties.viewPositions, viewPositionsBuffer);
            interpolationComputeShader.SetBuffer(kernelBackFlicker, interpolationProperties.viewOffsets, viewOffsetsBuffer);
            interpolationComputeShader.SetBuffer(kernelBackFlicker, interpolationProperties.baseViewPositions, baseViewPositionsBuffer);

            uint blockX, blockY, blockZ;
            interpolationComputeShader.GetKernelThreadGroupSizes(kernelFwd, out blockX, out blockY, out blockZ);
            int computeX = renderSettings.ViewWidth / (int) blockX + Mathf.Min(renderSettings.ViewWidth % (int) blockX, 1);
            int computeY = renderSettings.ViewHeight / (int) blockY + Mathf.Min(renderSettings.ViewHeight % (int) blockY, 1);
            int computeZ = viewCount / (int) blockZ + Mathf.Min(viewCount % (int) blockZ, 1);

            if (optimization.ReduceFlicker) {
                int spanSize = 2 * viewInterpolation;
                interpolationComputeShader.SetInt(interpolationProperties.spanSize, spanSize);
                for (int i = 0; i < spanSize; i++) {
                    interpolationComputeShader.SetInt(interpolationProperties.px, i);
                    interpolationComputeShader.Dispatch(kernelFwd, renderSettings.ViewWidth / spanSize, computeY, computeZ);
                    interpolationComputeShader.Dispatch(kernelBack, renderSettings.ViewWidth / spanSize, computeY, computeZ);
                }
            } else {
                interpolationComputeShader.Dispatch(kernelFwdFlicker, computeX, computeY, computeZ);
                interpolationComputeShader.Dispatch(kernelBackFlicker, computeX, computeY, computeZ);
            }

            if (optimization.FillGaps) {
                var fillgapsKernel = interpolationComputeShader.FindKernel("FillGaps");
                interpolationComputeShader.SetTexture(fillgapsKernel, interpolationProperties.result, quilt);
                interpolationComputeShader.SetTexture(fillgapsKernel, interpolationProperties.resultDepth, quiltRTDepth);
                interpolationComputeShader.SetBuffer(fillgapsKernel, interpolationProperties.viewPositions, viewPositionsBuffer);
                interpolationComputeShader.Dispatch(fillgapsKernel, computeX, computeY, computeZ);
            }

            viewPositionsBuffer.Dispose();
            viewOffsetsBuffer.Dispose();
            baseViewPositionsBuffer.Dispose();
        }
        public static void SetLightfieldMaterialSettings(HologramCamera hologramCamera, Material lightfieldMaterial) {
            if (lightfieldMaterial == null)
                throw new ArgumentNullException(nameof(lightfieldMaterial));

            if (!lightfieldProperties.Initialized)
                lightfieldProperties.InitializeAll();

            Calibration cal = (hologramCamera.AreRenderSettingsLockedForRecording) ? hologramCamera.UnmodifiedCalibration : hologramCamera.Calibration;
            HologramRenderSettings renderSettings = hologramCamera.RenderSettings;
            float aspect = hologramCamera.Aspect;
            lightfieldMaterial.SetFloat(lightfieldProperties.pitch, cal.pitch);

            lightfieldMaterial.SetFloat(lightfieldProperties.slope, cal.slope);
            lightfieldMaterial.SetFloat(lightfieldProperties.center, cal.center
#if UNITY_EDITOR
                + hologramCamera.CameraProperties.CenterOffset
#endif
            );
            lightfieldMaterial.SetFloat(lightfieldProperties.subpixelSize, cal.subp);
            lightfieldMaterial.SetVector(lightfieldProperties.tile, new Vector4(
                renderSettings.viewColumns,
                renderSettings.viewRows,
                renderSettings.numViews,
                renderSettings.viewColumns * renderSettings.viewRows
            ));
            lightfieldMaterial.SetVector(lightfieldProperties.viewPortion, new Vector4(
                renderSettings.ViewPortionHorizontal,
                renderSettings.ViewPortionVertical
            ));

            lightfieldMaterial.SetVector(lightfieldProperties.aspect, new Vector4(
                aspect,
                aspect
            ));

// #if UNITY_EDITOR_OSX && UNITY_2019_3_OR_NEWER
//             lightfieldMaterial.SetFloat(lightfieldProperties.verticalOffset, (float) -21 / cal.screenHeight);
// #elif UNITY_EDITOR_OSX && UNITY_2019_1_OR_NEWER
//             lightfieldMaterial.SetFloat(lightfieldProperties.verticalOffset, (float) -19 / cal.screenHeight);
// #endif
            //TODO: Setting this to non-zero values (above) messes with the center, where you see the seam between 2 views in your LKG device.
            //Perhaps we don't need this verticalOffset uniform property anymore in the Lightfield shader,
            //and we can remove this code?
            lightfieldMaterial.SetFloat(lightfieldProperties.verticalOffset, 0);
        }
    }
}
