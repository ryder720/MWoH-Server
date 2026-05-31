# 0001 - Dynamic Card Evolution Suffixes

**Status**: accepted

To distinguish card fusion tiers (Base, Base+, Base++, Base+++) without corrupting backend gacha lookup tables and database query matching keys, we decided to keep `CardTemplate.Title` clean in the database (e.g. `"Deadlier Superior Carnage"`) and dynamically compute player-facing display names at the application boundary using a helper method `GetDisplayName()` which appends fusion suffixes dynamically parsed from `CardTemplate.VariantName`. This avoids high-risk database migrations and preserves critical gacha and template lookup logic while providing the correct visual progression indicators to players.
