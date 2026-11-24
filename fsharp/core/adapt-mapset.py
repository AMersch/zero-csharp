#!/usr/bin/env python3
# Makes map.fs and set.fs here from dotnet/fsharp's src/FSharp.Core/map.fs and
# set.fs (MIT): drops what zero mode lacks (serialization, debugger views,
# IDictionary, the span-based Set.Create, tracing) and SR's resource strings.
# The AVL trees and the Map and Set modules are unchanged.
#
# Usage: adapt-mapset.py <dotnet/fsharp checkout>/src/FSharp.Core <out dir>
import re, sys, os

src, out = sys.argv[1], sys.argv[2]

STRINGS = {
    'mapCannotBeMutated': 'Map values cannot be mutated.',
    'notComparable': 'The object is not comparable.',
    'setContainsNoElements': 'The set contains no elements.',
    'enumerationNotStarted': 'Enumeration has not started. Call MoveNext.',
    'enumerationAlreadyFinished': 'Enumeration already finished.',
    'keyNotFound': 'The given key was not present in the dictionary.',
    'keyNotFoundAlt': 'The item, key, or index was not found.',
}

def drop_ifdefs(s, symbol):
    # #if <symbol> ... #endif, with any #else part kept.
    pattern = re.compile(r'^#if %s\n(.*?)^#endif\n' % re.escape(symbol), re.S | re.M)
    def repl(m):
        body = m.group(1)
        if '\n#else\n' in '\n' + body:
            return body.split('#else\n', 1)[1]
        return ''
    return pattern.sub(repl, s)

def drop_block(s, start, stop_before):
    # From the line starting with `start` up to (not including) `stop_before`.
    i = s.index(start)
    j = s.index(stop_before, i)
    return s[:i] + s[j:]

def drop_interface(s, header):
    # An `interface X with` block at 4 spaces: up to the next line at 4 spaces
    # or less that isn't blank.
    i = s.index(header)
    m = re.compile(r'\n(?=    \S|\S)').search(s, i + len(header))
    return s[:i] + s[m.start() + 1:]

def common(s):
    s = drop_ifdefs(s, 'TRACE_SETS_AND_MAPS')
    s = drop_ifdefs(s, 'NETSTANDARD2_1_OR_GREATER || NET')
    s = re.sub(r'SR\.GetString\(SR\.(\w+)\)', lambda m: '"%s"' % STRINGS[m.group(1)], s)
    s = re.sub(r'^\s*\[<NonSerialized>\]\n', '', s, flags=re.M)
    s = re.sub(r'^\s*\[<DebuggerTypeProxy\([^\n]*\n', '', s, flags=re.M)
    s = re.sub(r'^\s*\[<DebuggerBrowsable\([^\n]*\n', '', s, flags=re.M)
    s = re.sub(r'^\s*let mutable serializedData = null\n', '', s, flags=re.M)
    s = s.replace('LanguagePrimitives.anyToStringShowingNull',
                  'Microsoft.FSharp.Primitives.Basics.Text.anyToStringShowingNull')
    s = s.replace('// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.',
                  '// Adapted by fsharp/core/adapt-mapset.py from dotnet/fsharp (MIT; Copyright (c) Microsoft\n// Corporation). Edit that script, not this file.')
    s = s.replace('open Microsoft.FSharp.Core\n', 'open Microsoft.FSharp.Core\nopen Microsoft.FSharp.Core.Operators\n', 1)
    # The serialization hooks, each up to its `serializedData <- null`, and the
    # comment on the field they used.
    for hook in ('OnSerializingAttribute', 'OnDeserializedAttribute'):
        s = re.sub(r'    \[<System\.Runtime\.Serialization\.%s>\].*?serializedData <- null\n\n?' % hook,
                   '', s, count=1, flags=re.S)
    s = re.sub(r'    // (NOTE: )?This type is logically immutable\. This field is only mutated during serialization and deserialization\.\n(    //.*\n)*\n',
               '', s)
    # Mutable only for deserialization.
    s = re.sub(r'    // (NOTE: )?This type is logically immutable\. This field is only mutated during deserialization\.\n    let mutable ',
               '    let ', s)
    # Inside FSharp.Core, an inline function's :?> and a seq { } refer to
    # UnboxGeneric and Seq.delay in a way fsc can't then find (FS0971,
    # "Undefined value"): castclass and Seq.map instead.
    s = re.sub(r"value :\?> (\w+TreeNode<[^>]*>)", r'(# "castclass !0" type (\1) value : \1 #)', s)
    s = re.sub(r"\(seq \{\n\s*for item in parent do\n\s*item\.(\w+)\n\s*\}\)",
               r"(Seq.map (fun (item: KeyValuePair<'Key, 'Value>) -> item.\1) parent)", s)
    return s

m = common(open(os.path.join(src, 'map.fs')).read())
m = m.replace('\nmodule MapTree =', '\nmodule internal MapTree =')
m = m.replace("\n    (comparer: IComparer<'Key>, tree: MapTree<'Key, 'Value>) =", "\n    internal (comparer: IComparer<'Key>, tree: MapTree<'Key, 'Value>) =")
m = drop_interface(m, '    interface IDictionary<\'Key, \'Value> with\n')
m = drop_interface(m, '    interface IReadOnlyDictionary<\'Key, \'Value> with\n')
m = drop_block(m, 'and [<Sealed>] MapDebugView', 'and KeyCollection')
open(os.path.join(out, 'map.fs'), 'w').write('#nowarn "35" "42" "44" "3370"\n' + m)

t = common(open(os.path.join(src, 'set.fs')).read())
t = t.replace("'T: comparison>(comparer: IComparer<'T>, tree: SetTree<'T>) =", "'T: comparison> internal (comparer: IComparer<'T>, tree: SetTree<'T>) =")
t = drop_block(t, 'and [<Sealed>] SetDebugView', '[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]\n[<RequireQualifiedAccess>]\nmodule Set =')
open(os.path.join(out, 'set.fs'), 'w').write('#nowarn "35" "42" "44" "3370"\n' + t)
