﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

public class AnimatorTextureBaker : MonoBehaviour
{
    public ComputeShader infoTexGen;
    public Shader playShader;

    public struct VertInfo
    {
        public Vector3 position;
        public Vector3 normal;
    }

    // Use this for initialization
    IEnumerator Start()
    {
        var animator = GetComponent<Animator>();
        var clips = animator.runtimeAnimatorController.animationClips;
        var skin = GetComponentInChildren<SkinnedMeshRenderer>();
        var vCount = skin.sharedMesh.vertexCount;
        var texWidth = Mathf.NextPowerOfTwo(vCount);
        var mesh = new Mesh();

        animator.speed = 0;
        foreach (var c in clips)
        {
            var frames = Mathf.NextPowerOfTwo((int)(c.length / 0.05f));
            var infoList = new List<VertInfo>();

            var pRt = new RenderTexture(texWidth, frames, 0, RenderTextureFormat.ARGBHalf);
            pRt.name = string.Format("{0}.{1}.posTex", name, c.name);
            var nRt = new RenderTexture(texWidth, frames, 0, RenderTextureFormat.ARGBHalf);
            nRt.name = string.Format("{0}.{1}.normTex", name, c.name);

            foreach (var rt in new[] { pRt, nRt })
            {
                rt.enableRandomWrite = true;
                rt.Create();
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
            }

            animator.Play(c.name);
            yield return 0;

            for (var i = 0; i < frames; i++)
            {
                animator.Play(c.name, 0, (float)i / frames);
                yield return 0;
                skin.BakeMesh(mesh);

                infoList.AddRange(Enumerable.Range(0, vCount)
                    .Select(idx => new VertInfo()
                    {
                        position = mesh.vertices[idx],
                        normal = mesh.normals[idx]
                    })
                );
            }
            var buffer = new ComputeBuffer(infoList.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(VertInfo)));
            buffer.SetData(infoList.ToArray());

            var kernel = infoTexGen.FindKernel("CSMain");
            uint x, y, z;
            infoTexGen.GetKernelThreadGroupSizes(kernel, out x, out y, out z);

            infoTexGen.SetInt("VertCount", vCount);
            infoTexGen.SetBuffer(kernel, "Info", buffer);
            infoTexGen.SetTexture(kernel, "OutPosition", pRt);
            infoTexGen.SetTexture(kernel, "OutNormal", nRt);
            infoTexGen.Dispatch(kernel, vCount / (int)x + 1, frames / (int)y + 1, 1);

            buffer.Release();

#if UNITY_EDITOR
            var folderName = "BakingSkinnedAnimationToTexture/BakedAnimationTex";
            var folderPath = Path.Combine("Assets", folderName);
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets", folderName);

            var subFolder = name;
            var subFolderPath = Path.Combine(folderPath, subFolder);
            if (!AssetDatabase.IsValidFolder(subFolderPath))
                AssetDatabase.CreateFolder(folderPath, subFolder);

            var posTex = RenderTextureToTexture2D.Convert(pRt);
            var normTex = RenderTextureToTexture2D.Convert(nRt);
            Graphics.CopyTexture(pRt, posTex);
            Graphics.CopyTexture(nRt, normTex);

            var mat = new Material(playShader);
            mat.SetTexture("_MainTex", skin.sharedMaterial.mainTexture);
            mat.SetTexture("_PosTex", posTex);
            mat.SetTexture("_NmlTex", normTex);
            mat.SetFloat("_Length", c.length);
            if (c.isLooping)
            {
                mat.SetFloat("_Loop", 1f);
                mat.EnableKeyword("ANIM_LOOP");
            }

            var go = new GameObject(name + "." + c.name);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshFilter>().sharedMesh = skin.sharedMesh;

            AssetDatabase.CreateAsset(posTex, Path.Combine(subFolderPath, pRt.name + ".asset"));
            AssetDatabase.CreateAsset(normTex, Path.Combine(subFolderPath, nRt.name + ".asset"));
            AssetDatabase.CreateAsset(mat, Path.Combine(subFolderPath, string.Format("{0}.{1}.animTex.asset", name, c.name)));
            PrefabUtility.CreatePrefab(Path.Combine(folderPath, go.name + ".prefab").Replace("\\", "/"), go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
        }
    }
}
