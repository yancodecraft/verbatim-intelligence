import { ref } from "vue";

// Module-level singleton: one session state for the whole app.
const email = ref<string | null>(null);
const checked = ref(false);

async function check(): Promise<void> {
	try {
		const response = await fetch("/api/auth/me");
		email.value = response.ok ? (await response.json()).email : null;
	} catch {
		email.value = null;
	} finally {
		checked.value = true;
	}
}

async function signOut(): Promise<void> {
	await fetch("/api/auth/logout", { method: "POST" });
	email.value = null;
}

export function useSession() {
	return { email, checked, check, signOut };
}

/** Test hook only: module-level state leaks between specs otherwise. */
export function resetSessionForTests(): void {
	email.value = null;
	checked.value = false;
}
