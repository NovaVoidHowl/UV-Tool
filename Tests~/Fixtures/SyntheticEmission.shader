Shader "Hidden/NVH/Tests/UnfamiliarEmission"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _EmissionMap ("Stored emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Stored emission colour", Color) = (0.75,1.24,3,1)
    }
    SubShader { Pass { } }
}
