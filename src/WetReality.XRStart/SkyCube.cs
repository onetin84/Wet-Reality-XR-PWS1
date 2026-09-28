// Himmel mit Wolken im Headset (XRStart 1.25.1), SkyFix "cubemap" (Vorgabe).
//
// GEMESSEN: RenderSettings.skybox = SKY_ProceduralSkybox_FL(_Fountain), Shader
// FuturLab/ProceduralCloud_FL (prozedural, Wolken). Er zeichnet im Headset
// nichts (Schlieren = ungeloeschter Puffer), flach schon. Stereo-Modus 1.13.0
// MultiPass, 1.25.0 SinglePassInstanced - ein Shader ohne Instancing zeichnet
// dort nur EIN Auge. 1.25.0: "Skybox/Cubemap" NICHT im Build (Shader.Find null).
//
// DARUM OHNE SKYBOX-SHADER: der Spielhimmel wird FLACH abgegriffen und auf einem
// WUERFEL AUS SECHS FLAECHEN um die Kamera gezeigt.
//   - Hilfskamera ohne XR (stereoTargetEye None, cullingMask 0, clearFlags
//     Skybox, 90 Grad, Seitenverhaeltnis 1, abgeschaltet) rendert je Frame EINE
//     Seite per Render() in eine 2D-RenderTexture (SkyFaceSize) - die Wolken
//     ziehen, der ganze Himmel ist alle 6 Frames frisch.
//   - Sechs Quads mit "Sprites/Default" (die Zeigestrahlen der Mod benutzen ihn,
//     im Headset auf beiden Augen), Warteschlange 1000 (Background), ohne
//     ZWrite: der Himmel zeichnet ZUERST und alles andere darueber, auch Ferne
//     hinter SkyDistance. Kollider entfernt (Teleport/Strahlen treffen sie nicht).
//   - Quad-Drehung = Drehung der Hilfskamera je Seite: dann stimmen rechts/oben
//     von Bild und Flaeche ueberein, ohne Spiegeln.
//   - Je Frame auf die Kameraposition gesetzt (Himmel ist unendlich fern).
// Die XR-Kamera loescht dabei SolidColor (SkyColor) - nur falls eine Luecke
// bleibt, ist sie hellblau statt verschmiert. Jeder Fehlschlag -> solid, Log
// HIMMEL-WUERFEL.

using UnityEngine;

namespace WetReality.XRStart;

public sealed partial class XRStart
{
    private const int SkyFaceSize = 1024;
    private const float SkyDistance = 500f;

    // Je Seite: Blickrichtung und Oben (fuer LookRotation).
    private static readonly Vector3[] SkyFaceDir = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
    private static readonly Vector3[] SkyFaceUp = { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.back, Vector3.forward };

    private Camera? skyCubeCam;
    private readonly RenderTexture?[] skyFaceRT = new RenderTexture?[6];
    private readonly Transform?[] skyFaceQuad = new Transform?[6];
    private GameObject? skyCubeRoot;
    private Material? skyGameMat;
    private bool skyCubeFailed, skyCubeActive;
    private int skyCubeFace, skyCubeFrames;

    // Aus TickSky (1x/s): Bau. false = Rueckfall solid.
    private bool EnsureSkyCube(Camera xrCam)
    {
        if (skyCubeFailed) return false;
        try
        {
            var game = RenderSettings.skybox;
            if (game == null) { HideSkyCube(); return false; }   // (noch) kein Spielhimmel - solid, KEIN Dauer-Aus
            skyGameMat = game;
            if (skyCubeRoot == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) return FailSkyCube("Shader 'Sprites/Default' nicht gefunden");
                var go = new GameObject("WetReality_SkyCubeCamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                skyCubeCam = go.AddComponent<Camera>();
                skyCubeCam.enabled = false;
                skyCubeCam.stereoTargetEye = StereoTargetEyeMask.None;
                skyCubeCam.cullingMask = 0;
                skyCubeCam.clearFlags = CameraClearFlags.Skybox;
                skyCubeCam.fieldOfView = 90f;
                skyCubeCam.aspect = 1f;
                skyCubeCam.nearClipPlane = 0.1f;
                skyCubeCam.farClipPlane = 1000f;

                skyCubeRoot = new GameObject("WetReality_SkyCube");
                UnityEngine.Object.DontDestroyOnLoad(skyCubeRoot);
                for (int i = 0; i < 6; i++)
                {
                    var rt = new RenderTexture(SkyFaceSize, SkyFaceSize, 16, RenderTextureFormat.ARGB32);
                    rt.wrapMode = TextureWrapMode.Clamp;   // keine Naht aus der Gegenkante
                    rt.name = "WetReality_SkyFace" + i;
                    rt.Create();
                    skyFaceRT[i] = rt;
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "SkyFace" + i;
                    var col = q.GetComponent<Collider>();
                    if (col != null) UnityEngine.Object.Destroy(col);
                    q.layer = 0;
                    q.transform.SetParent(skyCubeRoot.transform, false);
                    q.transform.localPosition = SkyFaceDir[i] * SkyDistance;
                    q.transform.localRotation = Quaternion.LookRotation(SkyFaceDir[i], SkyFaceUp[i]);
                    q.transform.localScale = new Vector3(2f * SkyDistance * 1.002f, 2f * SkyDistance * 1.002f, 1f);   // 90 Grad + Hauch Ueberlappung
                    var r = q.GetComponent<MeshRenderer>();
                    var m = new Material(sh);
                    m.mainTexture = rt;
                    m.renderQueue = 1000;   // Background: vor allem anderen
                    r.sharedMaterial = m;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    skyFaceQuad[i] = q.transform;
                }
                for (int i = 0; i < 6; i++) RenderSkyFace(i, xrCam);   // einmal ganz
                LoggerInstance.Msg($"HIMMEL-WUERFEL: gebaut - 6 Flaechen 'Sprites/Default' in {SkyDistance:F0} m, {SkyFaceSize}x{SkyFaceSize} je Seite, Spielhimmel '{game.name}' ({(game.shader == null ? "?" : game.shader.name)}) flach abgegriffen");
            }
            if (!skyCubeRoot.activeSelf) skyCubeRoot.SetActive(true);
            skyCubeActive = true;
            return true;
        }
        catch (Exception e) { return FailSkyCube(e.GetType().Name + ": " + e.Message); }
    }

    private bool FailSkyCube(string why)
    {
        skyCubeFailed = true;
        LoggerInstance.Warning("HIMMEL-WUERFEL: " + why + " - Rueckfall auf feste Farbe (solid)");
        HideSkyCube();
        return false;
    }

    // Aus OnUpdate, je Frame: eine Seite nachziehen.
    private void TickSkyCube()
    {
        if (!skyCubeActive || skyCubeFailed || skyCam == null) return;
        skyCubeFace = (skyCubeFace + 1) % 6;
        try { RenderSkyFace(skyCubeFace, skyCam); }
        catch (Exception e) { FailSkyCube("Render: " + e.GetType().Name + ": " + e.Message); return; }
        skyCubeFrames++;
        if (skyCubeFrames == 60) LoggerInstance.Msg("HIMMEL-WUERFEL: 60 Seiten nachgezogen, laeuft");
    }

    private void RenderSkyFace(int i, Camera xrCam)
    {
        if (skyCubeCam == null || skyFaceRT[i] == null) return;
        var t = skyCubeCam.transform;
        t.position = xrCam.transform.position;
        t.rotation = Quaternion.LookRotation(SkyFaceDir[i], SkyFaceUp[i]);
        skyCubeCam.targetTexture = skyFaceRT[i];
        skyCubeCam.Render();
        skyCubeCam.targetTexture = null;
    }

    // In onBeforeRender: der Wuerfel sitzt um die finale Kamera.
    private void PlaceSkyCube()
    {
        if (!skyCubeActive || skyCubeRoot == null || skyCam == null) return;
        try { skyCubeRoot.transform.SetPositionAndRotation(skyCam.transform.position, Quaternion.identity); }
        catch { skyCubeRoot = null; skyCubeActive = false; }
    }

    private void HideSkyCube()
    {
        skyCubeActive = false;
        try { if (skyCubeRoot != null && skyCubeRoot.activeSelf) skyCubeRoot.SetActive(false); } catch { skyCubeRoot = null; }
    }

    // Aus RestoreSky.
    private void StopSkyCube() => HideSkyCube();
}
