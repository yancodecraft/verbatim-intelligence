import { createRouter, createWebHistory } from "vue-router";
import { useSession } from "../session";
import HomeView from "../views/HomeView.vue";
import SignInView from "../views/SignInView.vue";
import VerifyView from "../views/VerifyView.vue";

const router = createRouter({
	history: createWebHistory(import.meta.env.BASE_URL),
	routes: [
		{
			path: "/",
			name: "home",
			component: HomeView,
			meta: { requiresAuth: true },
		},
		{
			path: "/sign-in",
			name: "sign-in",
			component: SignInView,
		},
		{
			// The path mailed inside magic links (see AuthEndpoints).
			path: "/verify",
			name: "verify",
			component: VerifyView,
		},
	],
});

router.beforeEach(async (to) => {
	if (!to.meta.requiresAuth) {
		return true;
	}
	const session = useSession();
	if (!session.checked.value) {
		await session.check();
	}
	return session.email.value !== null || { name: "sign-in" };
});

export default router;
