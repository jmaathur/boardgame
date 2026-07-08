/**
 * Forge v0 single-page UI, served verbatim by the backend as one self-contained
 * HTML document (inline CSS + vanilla JS, no build step / no bundler).
 *
 * Scope is browse + validate + build only (implementation-plan §2 A3, Forge v0).
 * Full form editing is a later milestone (Forge v1) and is intentionally absent.
 */
export const INDEX_HTML = `<!doctype html>
<html lang="en">
<head>
	<meta charset="utf-8" />
	<meta name="viewport" content="width=device-width, initial-scale=1" />
	<title>Forge — content editor</title>
	<style>
		:root {
			--bg: #f4f5f7;
			--panel: #ffffff;
			--ink: #1c2330;
			--muted: #5b6472;
			--line: #d8dce3;
			--accent: #2f6feb;
			--ok-bg: #e7f6ec;
			--ok-ink: #1c6b3a;
			--ok-line: #9fd8b3;
			--err-bg: #fdeceb;
			--err-ink: #a01d18;
			--err-line: #f0b3ae;
			--chip: #eef1f6;
		}
		* { box-sizing: border-box; }
		body {
			margin: 0;
			font: 14px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
			color: var(--ink);
			background: var(--bg);
		}
		header {
			display: flex;
			align-items: baseline;
			gap: 12px;
			padding: 14px 22px;
			background: var(--panel);
			border-bottom: 1px solid var(--line);
			position: sticky;
			top: 0;
			z-index: 5;
		}
		header h1 { margin: 0; font-size: 18px; letter-spacing: 0.2px; }
		header .sub { color: var(--muted); font-size: 12px; }
		.layout { display: flex; align-items: flex-start; }
		nav {
			position: sticky;
			top: 53px;
			flex: 0 0 160px;
			padding: 18px 14px;
			height: calc(100vh - 53px);
		}
		nav a {
			display: block;
			padding: 6px 10px;
			color: var(--muted);
			text-decoration: none;
			border-radius: 6px;
			font-weight: 500;
		}
		nav a:hover { background: var(--chip); color: var(--ink); }
		main { flex: 1 1 auto; padding: 18px 22px 80px; min-width: 0; }
		section { margin-bottom: 34px; scroll-margin-top: 70px; }
		section > h2 {
			font-size: 15px;
			text-transform: uppercase;
			letter-spacing: 0.6px;
			color: var(--muted);
			border-bottom: 1px solid var(--line);
			padding-bottom: 6px;
			margin: 0 0 14px;
		}
		.banner {
			border: 1px solid var(--line);
			border-radius: 8px;
			padding: 12px 16px;
			font-weight: 600;
		}
		.banner.ok { background: var(--ok-bg); color: var(--ok-ink); border-color: var(--ok-line); }
		.banner.err { background: var(--err-bg); color: var(--err-ink); border-color: var(--err-line); }
		.banner .issues { margin: 10px 0 0; font-weight: 400; }
		.banner .issues li { margin: 4px 0; }
		.banner code { background: rgba(0,0,0,0.06); padding: 1px 5px; border-radius: 4px; }
		.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 14px; }
		.card {
			background: var(--panel);
			border: 1px solid var(--line);
			border-radius: 8px;
			padding: 14px 16px;
		}
		.card h3 { margin: 0 0 2px; font-size: 15px; }
		.card .id { color: var(--muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; }
		.chips { display: flex; flex-wrap: wrap; gap: 6px; margin: 10px 0; }
		.chip {
			background: var(--chip);
			border-radius: 999px;
			padding: 2px 9px;
			font-size: 12px;
			color: var(--ink);
			white-space: nowrap;
		}
		.sub-list { margin: 8px 0 0; padding: 0; list-style: none; }
		.sub-list li {
			font-size: 12.5px;
			padding: 4px 0;
			border-top: 1px dashed var(--line);
			color: var(--muted);
		}
		.sub-list li strong { color: var(--ink); }
		.sub-label {
			font-size: 11px;
			text-transform: uppercase;
			letter-spacing: 0.5px;
			color: var(--muted);
			margin-top: 10px;
		}
		.rules dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; }
		.rules dt { color: var(--muted); }
		.rules dd { margin: 0; font-variant-numeric: tabular-nums; }
		button.build {
			background: var(--accent);
			color: #fff;
			border: 0;
			border-radius: 8px;
			padding: 10px 20px;
			font-size: 14px;
			font-weight: 600;
			cursor: pointer;
		}
		button.build:disabled { opacity: 0.6; cursor: default; }
		#build-result { margin-top: 12px; }
		.mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
		.empty { color: var(--muted); font-style: italic; }
	</style>
</head>
<body>
	<header>
		<h1>Forge</h1>
		<span class="sub">local content editor — browse · validate · build (v0)</span>
	</header>
	<div class="layout">
		<nav>
			<a href="#report">Report</a>
			<a href="#units">Units</a>
			<a href="#commanders">Commanders</a>
			<a href="#rules">Match Rules</a>
			<a href="#build">Build</a>
		</nav>
		<main>
			<section id="report">
				<h2>Validation Report</h2>
				<div id="report-banner" class="banner">Loading catalog…</div>
			</section>
			<section id="units">
				<h2>Units (<span id="unit-count">…</span>)</h2>
				<div id="unit-grid" class="grid"></div>
			</section>
			<section id="commanders">
				<h2>Commanders (<span id="commander-count">…</span>)</h2>
				<div id="commander-grid" class="grid"></div>
			</section>
			<section id="rules">
				<h2>Match Rules</h2>
				<div id="rules-body" class="card rules"><span class="empty">…</span></div>
			</section>
			<section id="build">
				<h2>Build</h2>
				<p class="sub" style="color:var(--muted);margin-top:0">
					Writes <span class="mono">dist/catalog.json</span> +
					<span class="mono">dist/catalog.hash</span> when the catalog validates.
				</p>
				<button id="build-btn" class="build">Build catalog</button>
				<div id="build-result"></div>
			</section>
		</main>
	</div>
	<script>
		const $ = (id) => document.getElementById(id);
		const esc = (s) => String(s).replace(/[&<>"]/g, (c) => (
			{ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]
		));
		const short = (h) => (h ? String(h).slice(0, 12) : "");

		function scaled(v) {
			if (v == null) return "—";
			if (typeof v === "number") return String(v);
			return v.base + "+" + v.perLevel + "/lvl";
		}

		function fireSummary(w) {
			const f = w.fire || {};
			if (f.mode === "instant") return "instant";
			if (f.mode === "volley") return "volley x" + f.count;
			if (f.mode === "beam") return "beam @" + f.tickIntervalS + "s";
			return f.mode || "?";
		}

		function renderReport(data) {
			const el = $("report-banner");
			const report = data.report || { ok: false, schemaErrors: [], lintIssues: [] };
			if (report.ok) {
				const units = data.catalog
					? data.catalog.packs.reduce((n, p) => n + p.units.length, 0)
					: 0;
				el.className = "banner ok";
				el.innerHTML = "catalog valid — " + units + " units, hash <code>" +
					esc(short(data.hash)) + "</code>";
				return;
			}
			const rows = [];
			for (const e of report.schemaErrors || []) {
				rows.push("<li>schema · <code>" + esc(e.file) + "</code> — " + esc(e.error) + "</li>");
			}
			for (const i of report.lintIssues || []) {
				rows.push("<li>lint · <code>" + esc(i.path) + "</code> — " + esc(i.message) + "</li>");
			}
			el.className = "banner err";
			el.innerHTML = "catalog INVALID — " +
				(report.schemaErrors || []).length + " schema error(s), " +
				(report.lintIssues || []).length + " lint issue(s)" +
				'<ul class="issues">' + rows.join("") + "</ul>";
		}

		function renderUnits(catalog) {
			const grid = $("unit-grid");
			if (!catalog) {
				$("unit-count").textContent = "0";
				grid.innerHTML = '<p class="empty">Catalog did not build — fix the errors above.</p>';
				return;
			}
			const units = catalog.packs.flatMap((p) => p.units);
			$("unit-count").textContent = String(units.length);
			grid.innerHTML = units.map((u) => {
				const fp = u.placement.footprint;
				const chips = [
					"tier " + u.tier,
					"cost " + u.cost.deployCost,
					fp.w + "×" + fp.h,
					u.placement.domain,
					"squad " + u.squad.count,
				].map((c) => '<span class="chip">' + esc(c) + "</span>").join("");
				const weapons = (u.member.weapons || []).map((w) =>
					"<li><strong>" + esc(w.id) + "</strong> · rng " + esc(w.range) +
					" · dmg " + esc(scaled(w.damage)) + " · " + esc(fireSummary(w)) +
					" · [" + esc((w.targets || []).join(", ")) + "]</li>"
				).join("");
				const abilities = (u.member.abilities || []).map((a) =>
					"<li><strong>" + esc(a.id) + "</strong> · trigger " +
					esc(a.trigger && a.trigger.kind) + "</li>"
				).join("");
				let body = '<div class="card"><h3>' + esc(u.name) + '</h3>' +
					'<div class="id">' + esc(u.id) + "</div>" +
					'<div class="chips">' + chips + "</div>";
				body += '<div class="sub-label">Weapons</div>';
				body += weapons
					? '<ul class="sub-list">' + weapons + "</ul>"
					: '<div class="empty">none</div>';
				body += '<div class="sub-label">Abilities</div>';
				body += abilities
					? '<ul class="sub-list">' + abilities + "</ul>"
					: '<div class="empty">none</div>';
				return body + "</div>";
			}).join("");
		}

		function abilitySummary(a) {
			if (a.kind === "statMod") {
				const targets = a.unitFilter && a.unitFilter.length
					? a.unitFilter.join(", ") : "all units";
				return "statMod → " + targets + " (" +
					(a.mods || []).map((m) => m.stat).join(", ") + ")";
			}
			if (a.kind === "economyMod") {
				const parts = [];
				if (a.incomePerRoundAdd) parts.push("+" + a.incomePerRoundAdd + " income/round");
				if (a.deploySlotsAdd) parts.push("+" + a.deploySlotsAdd + " deploy");
				if (a.unlockSlotsAdd) parts.push("+" + a.unlockSlotsAdd + " unlock");
				if (a.startingIncomeAdd) parts.push("+" + a.startingIncomeAdd + " start$");
				return "economyMod → " + (parts.join(", ") || "no-op");
			}
			return a.kind;
		}

		function renderCommanders(rules) {
			const grid = $("commander-grid");
			const list = (rules && rules.commanders) || [];
			$("commander-count").textContent = String(list.length);
			if (!list.length) {
				grid.innerHTML = '<p class="empty">No commanders.</p>';
				return;
			}
			grid.innerHTML = list.map((c) => {
				const abilities = (c.ability || []).map(
					(a) => '<li>' + esc(abilitySummary(a)) + "</li>"
				).join("");
				return '<div class="card"><h3>' + esc(c.name) + '</h3>' +
					'<div class="id">' + esc(c.id) + "</div>" +
					'<div class="chips"><span class="chip">hp ' + esc(c.hp) +
					'</span><span class="chip">' + esc((c.startingUnits || []).length) +
					' starting units</span></div>' +
					'<div class="sub-label">Ability</div>' +
					(abilities ? '<ul class="sub-list">' + abilities + "</ul>"
						: '<div class="empty">none</div>') +
					"</div>";
			}).join("");
		}

		function renderRules(rules) {
			const body = $("rules-body");
			if (!rules) { body.innerHTML = '<span class="empty">unavailable</span>'; return; }
			const t = rules.timers || {};
			const inc = rules.income || {};
			body.innerHTML = "<dl>" +
				"<dt>Board</dt><dd>" + esc(rules.board.w) + " × " + esc(rules.board.h) + "</dd>" +
				"<dt>Income / round</dt><dd>+" + esc(inc.perRoundIncrement) +
				" (start " + esc(inc.startingIncome) + ", carryOver " + esc(inc.carryOver) + ")</dd>" +
				"<dt>Deploys / round</dt><dd>" + esc(rules.deploysPerRound) + "</dd>" +
				"<dt>Unlocks / round</dt><dd>" + esc(rules.unlocksPerRound) + "</dd>" +
				"<dt>Timers</dt><dd>deploy " + esc(t.deploySeconds) + "s · battle " +
				esc(t.battleSeconds) + "s · results " + esc(t.resultsHoldSeconds) +
				"s · pick " + esc(t.commanderPickSeconds) + "s</dd>" +
				"<dt>Commanders offered</dt><dd>" + esc(rules.commandersOffered) + "</dd>" +
				"</dl>";
		}

		async function loadCatalog() {
			try {
				const res = await fetch("/api/catalog");
				const data = await res.json();
				renderReport(data);
				renderUnits(data.catalog);
				renderCommanders(data.catalog && data.catalog.matchRules);
				renderRules(data.catalog && data.catalog.matchRules);
			} catch (err) {
				$("report-banner").className = "banner err";
				$("report-banner").textContent = "failed to load catalog: " + err;
			}
		}

		async function build() {
			const btn = $("build-btn");
			const out = $("build-result");
			btn.disabled = true;
			out.innerHTML = '<div class="banner">Building…</div>';
			try {
				const res = await fetch("/api/build", { method: "POST" });
				const data = await res.json();
				if (data.ok) {
					out.innerHTML = '<div class="banner ok">Built — wrote dist/catalog.json, hash <code>' +
						esc(short(data.hash)) + "</code></div>";
					// Refresh the report banner too (hash may be fresh).
					loadCatalog();
				} else {
					const report = data.report || {};
					const rows = []
						.concat((report.schemaErrors || []).map(
							(e) => "<li>schema · <code>" + esc(e.file) + "</code> — " + esc(e.error) + "</li>"))
						.concat((report.lintIssues || []).map(
							(i) => "<li>lint · <code>" + esc(i.path) + "</code> — " + esc(i.message) + "</li>"));
					out.innerHTML = '<div class="banner err">Build failed — nothing written.<ul class="issues">' +
						rows.join("") + "</ul></div>";
				}
			} catch (err) {
				out.innerHTML = '<div class="banner err">Build request failed: ' + esc(err) + "</div>";
			} finally {
				btn.disabled = false;
			}
		}

		$("build-btn").addEventListener("click", build);
		loadCatalog();
	</script>
</body>
</html>`;
