export interface Representative {
	position: number;
	text: string;
}

export interface Theme {
	name: string;
	synthesis: string;
	verbatimCount: number;
	representatives: Representative[];
}
