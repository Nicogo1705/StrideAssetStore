// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace StrideAssetStore.Core.Models;

/// <summary>Outcome of a publish attempt.</summary>
/// <param name="Success">Whether the PR was opened.</param>
/// <param name="PullRequestUrl">The PR URL on success.</param>
/// <param name="Error">A human-readable error on failure.</param>
public sealed record PublishResult(bool Success, string? PullRequestUrl, string? Error);
