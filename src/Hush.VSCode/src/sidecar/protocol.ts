/**
 * TypeScript mirrors of the .NET DTOs in `src/Hush.VSCode.Sidecar/Protocol/Messages.cs`.
 * Field names use camelCase to match StreamJsonRpc's CamelCase naming policy.
 */

export interface CategoryDto {
  key: string;
  displayName: string;
  isBuiltIn: boolean;
  enabled: boolean;
  style: MuteStyleDto;
}

export interface CategoryStateDto {
  key: string;
  enabled: boolean;
}

export interface MuteStyleDto {
  foreground?: string | null;
  background?: string | null;
  opacity: number;
  fontSizePercent: number;
  typeface?: string | null;
  bold: boolean;
  italic: boolean;
  autoCollapse: boolean;
}

export interface InitializeRequest {
  rulesPath?: string;
  workspaceFolders?: string[];
  initialState?: CategoryStateDto[];
  exclusionsEnabled?: boolean;
}

export interface InitializeResponse {
  categories: CategoryDto[];
  stateVersion: number;
  ruleSetVersion: number;
  exclusionsEnabled: boolean;
  tsCallRules: TsCallRuleDto[];
}

export interface TsCallRuleDto {
  name: string;
  category: string;
  receiverGlob?: string | null;
  methodNameGlob?: string | null;
  scope: MuteScope;
}

export interface DidOpenRequest {
  uri: string;
  languageId: string;
  version: number;
  content: string;
}

export interface TextChangeDto {
  start: number;
  length: number;
  text: string;
}

export interface DidChangeRequest {
  uri: string;
  version: number;
  changes: TextChangeDto[];
}

export interface DidCloseRequest {
  uri: string;
}

export interface GetSpansRequest {
  uri: string;
  version?: number;
}

export type MuteScope = 'Match' | 'WholeStatement' | 'ArgumentList' | 'SignatureRange';

export interface MuteSpanDto {
  start: number;
  end: number;
  categoryKey: string;
  ruleName: string;
  scope: MuteScope;
}

export interface GetSpansResponse {
  uri: string;
  version: number;
  stateVersion: number;
  ruleSetVersion: number;
  spans: MuteSpanDto[];
}

export interface SetMuteStateRequest {
  categoryKey: string;
  enabled: boolean;
}

export interface SetExclusionsEnabledRequest {
  enabled: boolean;
}

export interface StateChangeResponse {
  stateVersion: number;
  exclusionsEnabled: boolean;
  categories: CategoryStateDto[];
}

export interface ReloadRulesRequest {
  path?: string;
}

export interface ReloadRulesResponse {
  ruleSetVersion: number;
  categories: CategoryDto[];
  tsCallRules: TsCallRuleDto[];
}

export const BuiltInCategoryKeys = {
  Telemetry: 'telemetry',
  Logging: 'logging',
  Signature: 'signature',
  Guards: 'guards',
} as const;

export type BuiltInCategoryKey =
  (typeof BuiltInCategoryKeys)[keyof typeof BuiltInCategoryKeys];
