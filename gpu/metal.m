// What samples/GpuAdd.cs's Gpu class calls on macOS: Metal, behind a small C API.
// The kernels are in a metallib that gpu/kernels.s embeds in the program.
#import <Metal/Metal.h>
#include <stdint.h>
#include <stdio.h>

extern const uint8_t gpu_metallib[], gpu_metallib_end[];

static id<MTLDevice> s_device;
static id<MTLCommandQueue> s_queue;
static id<MTLLibrary> s_library;
static NSMutableDictionary<NSString*, id<MTLComputePipelineState>>* s_pipelines;

// 0 on success; otherwise the error is on stderr.
int32_t gpu_open(void) {
    @autoreleasepool {
        if (s_device) return 0;
        s_device = MTLCreateSystemDefaultDevice();
        if (!s_device) { fprintf(stderr, "gpu: no Metal device\n"); return 1; }
        dispatch_data_t data = dispatch_data_create(gpu_metallib, (size_t)(gpu_metallib_end - gpu_metallib),
                                                    NULL, DISPATCH_DATA_DESTRUCTOR_DEFAULT);
        NSError* error = nil;
        s_library = [s_device newLibraryWithData:data error:&error];
        if (!s_library) { fprintf(stderr, "gpu: %s\n", error.localizedDescription.UTF8String); s_device = nil; return 1; }
        s_queue = [s_device newCommandQueue];
        s_pipelines = [NSMutableDictionary dictionary];
        return 0;
    }
}

// The device's name as UTF-16; returns its length (at most capacity).
int32_t gpu_device_name(uint16_t* out, int32_t capacity) {
    NSString* name = s_device.name ?: @"";
    NSUInteger length = MIN(name.length, (NSUInteger)capacity);
    [name getCharacters:out range:NSMakeRange(0, length)];
    return (int32_t)length;
}

// A buffer the CPU and GPU share (Apple silicon: the same memory). Returns a
// handle for gpu_run and gpu_buffer_free, and the buffer's address in *contents.
void* gpu_buffer_new(int64_t bytes, void** contents) {
    id<MTLBuffer> buffer = [s_device newBufferWithLength:(NSUInteger)(bytes > 0 ? bytes : 1)
                                                 options:MTLResourceStorageModeShared];
    if (!buffer) { *contents = NULL; return NULL; }
    *contents = buffer.contents;
    return (__bridge_retained void*)buffer;
}

void gpu_buffer_free(void* handle) {
    if (handle) (void)(__bridge_transfer id<MTLBuffer>)handle;
}

// One kernel argument, as samples/GpuAdd.cs's GpuArg lays it out: a buffer
// (kind 0, handle from gpu_buffer_new) or a scalar (kind 1, `size` bytes).
typedef struct {
    int32_t kind;
    int32_t size;
    void* handle;
    uint8_t bytes[8];
} gpu_arg;

// Runs kernel `name` (UTF-16) on a width x height grid of threads, in
// threadgroups of group_width x group_height (0 x 0: as many as the kernel
// allows, in one row for a 1D grid), argument i bound to
// [[buffer(i)]] and `shared_bytes` of threadgroup memory to [[threadgroup(0)]],
// and waits for it. Returns 0 and the GPU's time in *milliseconds, or 1.
int32_t gpu_run(const uint16_t* name, int32_t name_length, const gpu_arg* args, int32_t count,
                uint32_t width, uint32_t height, uint32_t group_width, uint32_t group_height,
                int32_t shared_bytes, double* milliseconds) {
    @autoreleasepool {
        NSString* key = [NSString stringWithCharacters:name length:(NSUInteger)name_length];
        id<MTLComputePipelineState> pipeline = s_pipelines[key];
        if (!pipeline) {
            id<MTLFunction> function = [s_library newFunctionWithName:key];
            if (!function) { fprintf(stderr, "gpu: no kernel named %s\n", key.UTF8String); return 1; }
            NSError* error = nil;
            pipeline = [s_device newComputePipelineStateWithFunction:function error:&error];
            if (!pipeline) { fprintf(stderr, "gpu: %s: %s\n", key.UTF8String, error.localizedDescription.UTF8String); return 1; }
            s_pipelines[key] = pipeline;
        }
        NSUInteger max = pipeline.maxTotalThreadsPerThreadgroup;
        MTLSize group = MTLSizeMake(group_width, group_height ? group_height : 1, 1);
        if (group_width == 0 && height <= 1) {
            group = MTLSizeMake(MIN(max, (NSUInteger)width), 1, 1);
        } else if (group_width == 0) { // 2D: a SIMD group wide, as tall as fits
            NSUInteger w = pipeline.threadExecutionWidth;
            group = MTLSizeMake(w, max / w, 1);
        }
        if (group.width * group.height > max) {
            fprintf(stderr, "gpu: %s: threadgroups of %lu x %lu, at most %lu threads\n", key.UTF8String,
                    (unsigned long)group.width, (unsigned long)group.height, (unsigned long)max);
            return 1;
        }
        id<MTLCommandBuffer> commands = [s_queue commandBuffer];
        id<MTLComputeCommandEncoder> encoder = [commands computeCommandEncoder];
        [encoder setComputePipelineState:pipeline];
        for (int32_t i = 0; i < count; i++) {
            if (args[i].kind == 0)
                [encoder setBuffer:(__bridge id<MTLBuffer>)args[i].handle offset:0 atIndex:(NSUInteger)i];
            else
                [encoder setBytes:args[i].bytes length:(NSUInteger)args[i].size atIndex:(NSUInteger)i];
        }
        // Metal wants threadgroup memory in multiples of 16 bytes.
        if (shared_bytes > 0) [encoder setThreadgroupMemoryLength:(NSUInteger)((shared_bytes + 15) & ~15) atIndex:0];
        [encoder dispatchThreads:MTLSizeMake(width, height ? height : 1, 1) threadsPerThreadgroup:group];
        [encoder endEncoding];
        [commands commit];
        [commands waitUntilCompleted];
        if (commands.error) {
            fprintf(stderr, "gpu: %s: %s\n", key.UTF8String, commands.error.localizedDescription.UTF8String);
            return 1;
        }
        *milliseconds = (commands.GPUEndTime - commands.GPUStartTime) * 1000.0;
        return 0;
    }
}

// The built-ins when a kernel runs on the CPU, one thread at a time: the
// thread's position is whatever Gpu.SetThreadId last set, and each thread is a
// threadgroup of one. A kernel that needs a real threadgroup (barriers, shared
// memory across threads) can only be checked against plain C# on the CPU.
static _Thread_local uint32_t s_x, s_y, s_width = 1, s_height = 1;
static _Thread_local uint8_t s_shared[32768];
uint32_t gpu_thread_x(void) { return s_x; }
uint32_t gpu_thread_y(void) { return s_y; }
uint32_t gpu_local_x(void) { return 0; }
uint32_t gpu_local_y(void) { return 0; }
uint32_t gpu_group_x(void) { return s_x; }
uint32_t gpu_group_y(void) { return s_y; }
uint32_t gpu_group_w(void) { return 1; }
uint32_t gpu_group_h(void) { return 1; }
uint32_t gpu_grid_w(void) { return s_width; }
uint32_t gpu_grid_h(void) { return s_height; }
void gpu_barrier(void) {}
void* gpu_shared(void) { return s_shared; }
void gpu_set_thread_id(uint32_t x, uint32_t y, uint32_t width, uint32_t height) {
    s_x = x; s_y = y; s_width = width; s_height = height;
}

// The atomics on the CPU (gpu/ll2air.py turns them into Metal's on the GPU).
// Each returns the value the memory held before.
#define GPU_ATOMIC(op, builtin, name, T) \
    T gpu_atomic_##op##_##name(T* p, T v) { return builtin(p, v, __ATOMIC_RELAXED); }
#define GPU_ATOMIC_INTS(op, builtin) GPU_ATOMIC(op, builtin, i32, int32_t) GPU_ATOMIC(op, builtin, u32, uint32_t)
GPU_ATOMIC_INTS(add, __atomic_fetch_add)
GPU_ATOMIC_INTS(sub, __atomic_fetch_sub)
GPU_ATOMIC_INTS(min, __atomic_fetch_min)
GPU_ATOMIC_INTS(max, __atomic_fetch_max)
GPU_ATOMIC_INTS(and, __atomic_fetch_and)
GPU_ATOMIC_INTS(or, __atomic_fetch_or)
GPU_ATOMIC_INTS(xor, __atomic_fetch_xor)
GPU_ATOMIC_INTS(xchg, __atomic_exchange_n)
float gpu_atomic_xchg_f32(float* p, float v) { float old; __atomic_exchange(p, &v, &old, __ATOMIC_RELAXED); return old; }
float gpu_atomic_add_f32(float* p, float v) {
    float old, sum;
    __atomic_load(p, &old, __ATOMIC_RELAXED);
    do sum = old + v;
    while (!__atomic_compare_exchange(p, &old, &sum, 1, __ATOMIC_RELAXED, __ATOMIC_RELAXED));
    return old;
}
#define GPU_CAS(name, T) \
    T gpu_atomic_cas_##name(T* p, T value, T comparand) { \
        __atomic_compare_exchange_n(p, &comparand, value, 0, __ATOMIC_RELAXED, __ATOMIC_RELAXED); \
        return comparand; \
    }
GPU_CAS(i32, int32_t)
GPU_CAS(u32, uint32_t)
