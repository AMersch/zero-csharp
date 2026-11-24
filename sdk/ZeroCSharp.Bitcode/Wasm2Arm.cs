// A WebAssembly object file's data and functions, for 32-bit Arm (Cortex-M):
// an ELF object file. Pointers and integers are 32 bits and share registers,
// as on the 386, so only the assembly's spelling differs: '@' starts a
// comment and C symbols have no leading underscore.
namespace ZeroBitcode;

sealed class Arm : Target
{
    public override string Tool => "zero-bitcode wasm2arm";
    public override string Comment => "@";
    public override string Prefix => "";
    public override bool ReadOnlyInFlash => true;
}
