/* What musl's sources expect from its internal features.h. Every function
   here is linked statically into one program, so visibility doesn't matter. */
#ifndef _FEATURES_H
#define _FEATURES_H
#define _BSD_SOURCE 1 /* M_PI_2 and the other constants */
#define hidden
#define weak
#define weak_alias(old, new) extern __typeof(old) new __attribute__((__alias__(#old)))
#endif
