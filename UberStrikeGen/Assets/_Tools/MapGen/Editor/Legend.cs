#if UNITY_EDITOR
using UnityEngine;
namespace MapGen {
  public static class Legend {
    public static readonly Color32 Floor       = new(0xC0,0xC0,0xC0,0xFF);
    public static readonly Color32 Wall        = new(0x00,0x00,0x00,0xFF);
    public static readonly Color32 GlassBridge = new(0x00,0xFF,0xFF,0xFF);
    public static readonly Color32 Water       = new(0x00,0x00,0xFF,0xFF);
    public static readonly Color32 Spawn       = new(0xFF,0xFF,0x00,0xFF);
    public static readonly Color32 JumpPad     = new(0x00,0xFF,0x00,0xFF);
    public static readonly Color32 Teleporter  = new(0xFF,0x00,0xFF,0xFF);
    public static readonly Color32 Health      = new(0xFF,0x00,0x00,0xFF);
    public static readonly Color32 Armor       = new(0xFF,0x7F,0x00,0xFF);
    public const float CellSizeMeters = 1f;                               // 1 px → 1 m
    public static bool Equals(Color32 a, Color32 b) => a.r==b.r && a.g==b.g && a.b==b.b && a.a==b.a;
  }
}
#endif
