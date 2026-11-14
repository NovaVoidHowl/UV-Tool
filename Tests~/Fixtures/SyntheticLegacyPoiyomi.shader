Shader "Poiyomi/Legacy Synthetic"
{
    Properties
    {
        [HideInInspector] shader_master_label ("Poiyomi 8.1", Float) = 0
        [ToggleUI] _EnableUDIMDiscardOptions ("Enable UDIM Discard", Float) = 1
        _UDIMDiscardUV ("Discard UV", Int) = 1
        _UDIMDiscardRow0_0 ("Row 0 Column 0", Float) = 1
    }
    SubShader { Pass { } }
}
