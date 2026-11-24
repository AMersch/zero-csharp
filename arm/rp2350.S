// The block the RP2350's boot ROM looks for in the first 4 KB of flash before
// it starts a program: an IMAGE_DEF that says "an Arm executable for the
// RP2350, run in secure state". With no other item, the ROM starts the
// program from the vector table at the start of flash, picolibc's.
//
// Values from the pico-sdk: boot_picobin_headers/include/boot/picobin.h, and
// pico_crt0/embedded_start_block.inc.S for the layout.
//
// The section name sorts after picolibc's vector table (.data.init.enter),
// which its linker script puts first in the boot flash.

    .section .init.zz_image_def, "a"
    .p2align 2
    .word 0xffffded3    // PICOBIN_BLOCK_MARKER_START
    // Item PICOBIN_BLOCK_ITEM_1BS_IMAGE_TYPE (0x42), 1 word long, with the
    // value EXE (0x1) | security S (0x2 << 4) | CPU ARM (0 << 8) | chip
    // RP2350 (0x1 << 12) = 0x1021.
    .byte 0x42, 0x01
    .hword 0x1021
    // PICOBIN_BLOCK_ITEM_2BS_LAST (0xff), with the size of the items before
    // it in words (1).
    .byte 0xff
    .hword 0x0001
    .byte 0
    .word 0             // the next block: none, this one links to itself
    .word 0xab123579    // PICOBIN_BLOCK_MARKER_END
