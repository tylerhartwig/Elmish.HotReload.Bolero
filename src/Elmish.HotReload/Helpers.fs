[<AutoOpen>]
module Elmish.HotReload.Helpers


module Option =
    let either f x = function
        | Some v -> f v
        | None -> x


module List =
    let tryRemove pred l =
        let rec r pred head rest =
            match rest with
            | [] -> None, head
            | item::tail ->
                if pred item then
                    (Some item), head @ tail
                else
                    r pred (head @ [item]) tail
        r pred [] l