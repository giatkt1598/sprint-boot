import createClient from 'openapi-fetch';
import type { components, paths } from './schema';

export type { components, paths };
export type SystemPing = components['schemas']['SystemPingDto'];

export const createApiClient = (baseUrl = '') => createClient<paths>({ baseUrl });
