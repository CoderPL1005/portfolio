import { Component, input } from '@angular/core';
@Component({ selector:'app-admin-empty-state', template:`<section><h2>{{ title() }}</h2><p>{{ message() }}</p></section>`, styles:`section{padding:3rem 1rem;border:1px dashed var(--color-border);border-radius:var(--radius-lg);text-align:center}h2{margin:0 0 .5rem}p{margin:0;color:var(--color-text-muted)}` })
export class AdminEmptyStateComponent { readonly title=input.required<string>(); readonly message=input.required<string>(); }
