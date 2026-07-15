<script setup lang="ts">
import { RouterLink } from "vue-router";
</script>

<template>
	<main class="privacy">
		<RouterLink :to="{ name: 'home' }">Back</RouterLink>
		<h2>Privacy policy</h2>

		<p>
			Verbatim Intelligence is operated by YANTECH ("we"). This page explains
			what personal data we process and your rights over it. Questions or
			requests: <a href="mailto:privacy@yantech.fr">privacy@yantech.fr</a>.
		</p>

		<h3>What we process</h3>
		<ul>
			<li>
				<strong>Your account e-mail</strong>, used only to sign you in through a
				one-time link.
			</li>
			<li>
				<strong>The verbatims you upload</strong>. These may contain personal
				data about your own respondents; you are responsible for having the
				right to submit them. We process them on your behalf.
			</li>
		</ul>

		<h3>How we use it</h3>
		<p>
			We authenticate you, group your verbatims into themes and write faithful
			syntheses, and let you share a report through a revocable link. We do not
			sell your data and we use no third-party advertising or analytics.
		</p>

		<h3>Third parties</h3>
		<ul>
			<li>
				<strong>Anthropic</strong> (United States) processes the verbatims with
				its Claude models to produce the themes. Under Anthropic's commercial
				terms, inputs and outputs are <strong>not used to train models</strong>
				and its operational logs are deleted after a short period.
			</li>
			<li>
				<strong>Scaleway</strong> (France, EU) hosts the application, stores the
				encrypted backups, and delivers the sign-in e-mails.
			</li>
		</ul>

		<h3>Retention</h3>
		<ul>
			<li>The raw CSV file is discarded as soon as the analysis is created.</li>
			<li>
				You can delete any analysis, and your whole account, at any time from
				the app — deletion is immediate and cascades to all related data.
			</li>
			<li>
				Encrypted backups are kept for at most 30 days, after which deleted data
				also disappears from them.
			</li>
		</ul>

		<h3>Your rights</h3>
		<p>
			You may access, correct, delete, or object to the processing of your
			personal data. Deleting your account is self-service; for anything else,
			write to <a href="mailto:privacy@yantech.fr">privacy@yantech.fr</a>.
		</p>
	</main>
</template>

<style scoped>
.privacy {
	max-width: 44rem;
}

.privacy h3 {
	margin-top: 1.5rem;
}

.privacy li {
	margin: 0.35rem 0;
}
</style>
