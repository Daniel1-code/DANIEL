using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    public static class DesignCodeFactory
    {
        public static IDesignCode Create(DesignCodeKind kind)
        {
            if (kind == DesignCodeKind.Aci318) return new Aci318Code();
            return new Eurocode2Code();
        }
    }
}
