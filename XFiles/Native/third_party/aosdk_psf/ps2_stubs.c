/*
 * ps2_stubs.c
 *
 * PS2 (SPU2 / PSF2) symbols referenced by psx_hw.c but never exercised on the
 * PS1 (PSF) engine. Stubbed so the linker is satisfied; the PS1 memory map
 * never reaches these code paths.
 *
 * Signatures match psx.h declarations.
 *
 * Audio Overload SDK (PSF engine) — (C) R. Belmont, R. Bannister.
 */

#include "psx.h"

unsigned short SPU2read(mips_cpu_context *cpu, unsigned long reg)
{
    (void)cpu; (void)reg;
    return 0;
}

void SPU2write(mips_cpu_context *cpu, unsigned long reg, unsigned short val)
{
    (void)cpu; (void)reg; (void)val;
}

void SPU2readDMA4Mem(mips_cpu_context *cpu, uint32 usPSXMem, int iSize)
{
    (void)cpu; (void)usPSXMem; (void)iSize;
}

void SPU2writeDMA4Mem(mips_cpu_context *cpu, uint32 usPSXMem, int iSize)
{
    (void)cpu; (void)usPSXMem; (void)iSize;
}

void SPU2readDMA7Mem(mips_cpu_context *cpu, uint32 usPSXMem, int iSize)
{
    (void)cpu; (void)usPSXMem; (void)iSize;
}

void SPU2writeDMA7Mem(mips_cpu_context *cpu, uint32 usPSXMem, int iSize)
{
    (void)cpu; (void)usPSXMem; (void)iSize;
}

void SPU2interruptDMA4(mips_cpu_context *cpu)
{
    (void)cpu;
}

void SPU2interruptDMA7(mips_cpu_context *cpu)
{
    (void)cpu;
}

uint32 psf2_get_loadaddr(void)
{
    return 0;
}

void psf2_set_loadaddr(uint32 value)
{
    (void)value;
}

uint32 psf2_load_file(mips_cpu_context *cpu, char *file, uint8 *buf, uint32 buflen)
{
    (void)cpu; (void)file; (void)buf; (void)buflen;
    return 0xffffffff;
}

uint32 psf2_load_elf(mips_cpu_context *cpu, uint8 *start, uint32 len)
{
    (void)cpu; (void)start; (void)len;
    return 0;
}
