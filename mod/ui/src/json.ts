// C# から JSON 文字列で届く値の読み取り。

/**
 * JSON 文字列を読む。空文字や壊れた値のときは fallback を返す（壊れた値で UI 全体を落とさない。次の更新で直る）。
 */
export function parseJson<T>(text: string, fallback: T): T {
  try {
    return JSON.parse(text) as T;
  } catch {
    return fallback;
  }
}
