#ifndef RETROAUDIO_H
#define RETROAUDIO_H

/*
 * RetroAudio - native chiptune decoder shim for X-Files.
 *
 * Wraps four decoders behind one C interface:
 *   - game-music-emu 0.6.5 (LGPL-2.1+) : console formats (SPC, GBS, NSF, VGM, ...)
 *   - libopenmpt 0.8.7   (BSD-3-Clause): tracker formats (MOD, XM, S3M, IT, ...)
 *   - aosdk engine_psf  (GPL-2.0+)     : PlayStation PSF (.psf/.minipsf)
 *   - lazyusf 1.2       (CC0-1.0)      : Nintendo 64 USF (.usf/.miniusf)
 *
 * Output is 44100 Hz, 16-bit, interleaved stereo (PSF is mono and up-mixed).
 * Build: see XFiles/Native/build-native.ps1
 */

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) && defined(RETROAUDIO_BUILD)
#  define RA_API __declspec(dllexport)
#elif defined(_WIN32)
#  define RA_API __declspec(dllimport)
#else
#  define RA_API
#endif

#include <stddef.h>
#include <stdint.h>

#define RA_SAMPLE_RATE 44100
#define RA_CHANNELS 2

/* Hard cap on a single rendered track, in seconds. */
#define RA_MAX_SECONDS 600

/* Result codes. Negative values are errors. */
#define RA_OK 0
#define RA_ERR_FORMAT -1 /* extension not supported */
#define RA_ERR_OPEN -2   /* data failed to open/parse */
#define RA_ERR_TRACK -3  /* track index out of range */
#define RA_ERR_NOMEM -4  /* allocation failure */
#define RA_ERR_INTERNAL -5

typedef struct RA_Handle RA_Handle;

/* Static version string, e.g. "retroaudio 1.0 (gme 0.6.5, libopenmpt 0.8.7)". */
RA_API const char* RA_GetVersion(void);

/* Returns 1 if the extension (lowercase, no dot) is handled by RetroAudio. */
RA_API int RA_IsSupportedExt(const char* ext);

/*
 * Open a format from memory. `ext` is the lowercase file extension without
 * dot and is used to pick the decoder backend. `baseDir` is the directory
 * containing the file (UTF-8, without trailing slash); PSF/USF decoders use it
 * to resolve sibling library files (.psflib/.usflib). Data is copied
 * internally, so the caller may free it after RA_Open returns.
 */
RA_API int RA_Open(const void* data, size_t size, const char* ext, const char* baseDir, RA_Handle** outHandle);

RA_API void RA_Free(RA_Handle* h);

RA_API int RA_GetSampleRate(RA_Handle* h);
RA_API int RA_GetChannels(RA_Handle* h);

/* Number of subsongs/tracks (>= 1). */
RA_API int RA_GetTrackCount(RA_Handle* h);

/* Estimated duration of `track` in seconds (may be 0 if unknown). */
RA_API double RA_GetDurationSec(RA_Handle* h, int track);

/* Track title into `out` (bounded by outSize); returns number of chars written. */
RA_API int RA_GetTrackTitle(RA_Handle* h, int track, char* out, size_t outSize);

/*
 * Render a full track into `pcm` (interleaved stereo int16 at RA_SAMPLE_RATE).
 * capacityFrames limits how much may be written. On success returns RA_OK and
 * stores the number of frames actually rendered in *framesWritten. *durationSec
 * is set to the rendered duration (may be 0). The track plays through once
 * (no infinite loop) with fade-out where the format supplies one.
 */
RA_API int RA_Render(RA_Handle* h, int track, int16_t* pcm, int capacityFrames,
                     int* framesWritten, double* durationSec);

/*
 * Streaming render (low-memory alternative to RA_Render). Start a fresh render
 * of `track`, then pull frames with RA_RenderFrames in small chunks. Track
 * position advances across calls. When *trackEnded is nonzero, rendering is
 * finished for this track and the loop should stop; RA_EndTrack may be called
 * early to abandon a render.
 */
RA_API int RA_BeginTrack(RA_Handle* h, int track);
RA_API int RA_RenderFrames(RA_Handle* h, int16_t* pcm, int capacityFrames,
                           int* framesWritten, int* trackEnded);
RA_API void RA_EndTrack(RA_Handle* h);

#ifdef __cplusplus
}
#endif

#endif /* RETROAUDIO_H */
