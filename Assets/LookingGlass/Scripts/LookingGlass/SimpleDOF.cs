//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LookingGlass {
	[ExecuteInEditMode]
    [HelpURL("https://docs.lookingglassfactory.com/Unity/Scripts/SimpleDOF/")]
	public class SimpleDOF : MonoBehaviour {

		public HologramCamera hologramCamera;
		[Header("Dof Curve")]
		public float start = -1.5f;
		public float dip = -0.5f;
		public float rise =  0.5f;
		public float end =  2.0f;
		[Header("Blur")]
		[Range(0f, 2f)] public float blurSize = 1.0f;
		public bool horizontalOnly = true;
		public bool testFocus;
		Material passdepthMat;
		Material boxBlurMat;
		Material finalpassMat;

		void OnEnable() {
			// check for LookingGlass
			if (hologramCamera == null) {
				hologramCamera = GetComponentInParent<HologramCamera>();
				if (hologramCamera == null) {
					enabled = false;
					Debug.LogWarning("[LookingGlass] Simple DOF needs to be on a LookingGlass capture's camera");
					return;
				}
			}
			passdepthMat = new Material(Shader.Find("LookingGlass/DOF/Pass Depth"));
			boxBlurMat   = new Material(Shader.Find("LookingGlass/DOF/Box Blur"));
			finalpassMat = new Material(Shader.Find("LookingGlass/DOF/Final Pass"));
		}

		void OnDisable() {
			DestroyImmediate(passdepthMat);
			DestroyImmediate(boxBlurMat);
			DestroyImmediate(finalpassMat);
		}

		public void DoDOF(RenderTexture src, RenderTexture srcDepth) {
			// // make sure the LookingGlass is capturing depth
			// hologramCamera.cam.depthTextureMode = DepthTextureMode.Depth;
			// passing shader vars
			Vector4 dofParams = new Vector4(start, dip, rise, end) * hologramCamera.CameraProperties.Size;
			dofParams = new Vector4(
				1.0f / (dofParams.x - dofParams.y),
				dofParams.y,
				dofParams.z,
				1.0f / (dofParams.w - dofParams.z)
			);
			boxBlurMat.SetVector("dofParams", dofParams);
			boxBlurMat.SetFloat("focalLength", hologramCamera.CameraProperties.FocalPlane);
			finalpassMat.SetInt("testFocus", testFocus ? 1 : 0);
			if (horizontalOnly)
				Shader.EnableKeyword("_HORIZONTAL_ONLY");
			else
				Shader.DisableKeyword("_HORIZONTAL_ONLY");

			// make the temporary pass rendertextures
			var fullres = RenderTexture.GetTemporary(src.width, src.height, 0);
			// var fullresDest = RenderTexture.GetTemporary(src.width, src.height, 0);
			var blur1 = RenderTexture.GetTemporary(src.width / 2, src.height / 2, 0);
			var blur2 = RenderTexture.GetTemporary(src.width / 3, src.height / 3, 0);
			var blur3 = RenderTexture.GetTemporary(src.width / 4, src.height / 4, 0);

			Shader.SetGlobalVector("ProjParams", new Vector4(
				1f, 
				hologramCamera.SingleViewCamera.nearClipPlane, 
				hologramCamera.SingleViewCamera.farClipPlane, 
				1f
			));

			var tile = new Vector4(
                hologramCamera.RenderSettings.viewColumns,
                hologramCamera.RenderSettings.viewRows,
                hologramCamera.RenderSettings.numViews,
                hologramCamera.RenderSettings.viewColumns * hologramCamera.RenderSettings.viewRows
            );
			var viewPortion = new Vector4(
                hologramCamera.RenderSettings.ViewPortionHorizontal,
                hologramCamera.RenderSettings.ViewPortionVertical
            );
			boxBlurMat.SetVector("tile", tile);
            boxBlurMat.SetVector("viewPortion", viewPortion);
			finalpassMat.SetVector("tile", tile);
            finalpassMat.SetVector("viewPortion", viewPortion);

			// passes: start with depth
			passdepthMat.SetTexture("QuiltDepth", srcDepth);
			Graphics.Blit(src, fullres, passdepthMat);

			// blur 1
			boxBlurMat.SetInt("blurPassNum", 0);
			boxBlurMat.SetFloat("blurSize", blurSize * 2f);
			Graphics.Blit(fullres, blur1, boxBlurMat);

			// blur 2
			boxBlurMat.SetInt("blurPassNum", 1);
			boxBlurMat.SetFloat("blurSize", blurSize * 3f);
			Graphics.Blit(fullres, blur2, boxBlurMat);

			// blur 3
			boxBlurMat.SetInt("blurPassNum", 2);
			boxBlurMat.SetFloat("blurSize", blurSize * 4f);
			Graphics.Blit(fullres, blur3, boxBlurMat);

			// setting textures
			finalpassMat.SetTexture("blur1", blur1);
			finalpassMat.SetTexture("blur2", blur2);
			finalpassMat.SetTexture("blur3", blur3);

			// final blit for foreground
			// Graphics.Blit(fullres, src);
			Graphics.Blit(fullres, src, finalpassMat);

			// disposing of stuff
			RenderTexture.ReleaseTemporary(fullres);
			// RenderTexture.ReleaseTemporary(fullresDest);
			RenderTexture.ReleaseTemporary(blur1);
			RenderTexture.ReleaseTemporary(blur2);
			RenderTexture.ReleaseTemporary(blur3);
		}
	}
}