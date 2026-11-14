Shader ".poiyomi/Poiyomi Toon Synthetic"
{
    Properties
    {
        [HideInInspector] shader_master_label ("Poiyomi 10.0.29", Float) = 0
        [ToggleUI] _EnableUDIMDiscardOptions ("Enable UDIM Discard", Float) = 1
        _UDIMDiscardUV ("Discard UV", Int) = 1
        _UDIMDiscardRow0_0 ("Row 0 Column 0", Float) = 0
        _UDIMDiscardRow0_1 ("Row 0 Column 1", Float) = 1
        [ToggleUI] _EnableDissolve ("Enable Dissolve", Float) = 1
        [ToggleUI] _UVTileDissolveEnabled ("UV Tile Dissolve", Float) = 1
        _UVTileDissolveUV ("Dissolve UV", Int) = 0
        _UVTileDissolveAlpha_Row0_0 ("Row 0 Column 0", Range(-1, 1)) = 0.5
        _UVTileDissolveAlpha_Row0_1 ("Row 0 Column 1", Range(-1, 1)) = 0
    }
    SubShader { Pass { } }
}
