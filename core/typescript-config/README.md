# @core/typescript-config

Shared `tsconfig.json` bases for the monorepo.

- `base.json` — strict defaults for any TypeScript package.
- `bun.json` — extends `base.json` with Bun runtime types (consumers must have
  `@types/bun` in their `devDependencies`).

Usage in a package's `tsconfig.json`:

```json
{
	"extends": "@core/typescript-config/bun.json",
	"include": ["src/**/*.ts"]
}
```
