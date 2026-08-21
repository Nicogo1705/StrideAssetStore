// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace StrideAssetStore.Core.Models;

/// <summary>Human-readable byte sizes, shared so the same asset never reads two different ways.</summary>
public static class ByteSize
{
    /// <summary>Formats a byte count, e.g. <c>1.5 GB</c>, <c>820 KB</c>, <c>17 B</c>.</summary>
    public static string Format(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
